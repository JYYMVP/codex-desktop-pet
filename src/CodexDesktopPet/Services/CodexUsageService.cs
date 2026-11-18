using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexDesktopPet.Models;

namespace CodexDesktopPet.Services;

internal sealed class CodexUsageService(AppSettings settings)
{
    public async Task<string> GetPlanSummaryAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.CodexExecutable,
            // Stdio JSONL is the default transport.  Avoid going through the
            // optional --listen parser because Windows app-execution aliases
            // have behaved differently across Codex Desktop releases.
            Arguments = "app-server",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // Encoding.UTF8 emits a BOM when StreamWriter first writes.  The
            // app-server expects the first byte of each JSONL stream to be '{'.
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Codex app-server 启动失败。");
            AppLog.Write($"Codex usage app-server started. Pid={process.Id}; Executable={TryGetProcessPath(process)}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"找不到 Codex CLI。请检查 {AppSettings.SettingsPath} 中的 CodexExecutable。", ex);
        }

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await SendAsync(process, new
            {
                id = 1,
                method = "initialize",
                @params = new
                {
                    clientInfo = new
                    {
                        name = "codex-desktop-pet",
                        version = "0.1.0"
                    }
                }
            }, timeout.Token);
            AppLog.Write("Codex usage initialize request sent.");

            using var initializeResponse = await ReadResponseAsync(process, 1, timeout.Token);
            ThrowIfRpcError(initializeResponse.RootElement, "Codex app-server 初始化失败");
            AppLog.Write("Codex usage initialize response received.");

            await SendAsync(process, new
            {
                method = "initialized"
            }, timeout.Token);

            await SendAsync(process, new
            {
                id = 2,
                method = "account/rateLimits/read"
            }, timeout.Token);
            AppLog.Write("Codex usage rate-limit request sent.");

            using var usageResponse = await ReadResponseAsync(process, 2, timeout.Token);
            ThrowIfRpcError(usageResponse.RootElement, "Codex 套餐额度查询失败");
            AppLog.Write("Codex usage rate-limit response received.");
            return FormatSummary(usageResponse.RootElement);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Codex 套餐额度查询超时。");
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch { }

            try { await stderrTask; } catch { }
        }
    }

    private static string TryGetProcessPath(Process process)
    {
        try { return process.MainModule?.FileName ?? "unknown"; }
        catch { return "unavailable"; }
    }

    private static async Task SendAsync(Process process, object message,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static async Task<JsonDocument> ReadResponseAsync(Process process, int requestId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
                throw new InvalidOperationException("Codex app-server 在返回额度前退出。");

            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            if (document.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.Number && id.GetInt32() == requestId)
                return document;

            document.Dispose();
        }
    }

    private static void ThrowIfRpcError(JsonElement response, string prefix)
    {
        if (!response.TryGetProperty("error", out var error))
            return;

        var detail = error.TryGetProperty("message", out var message)
            ? message.GetString()
            : error.ToString();
        throw new InvalidOperationException($"{prefix}：{detail}");
    }

    private static string FormatSummary(JsonElement response)
    {
        if (!response.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("rateLimits", out var limits))
            throw new InvalidOperationException("Codex 没有返回可用的套餐额度信息。");

        var lines = new List<string> { "Codex 套餐额度" };
        if (limits.TryGetProperty("planType", out var plan) &&
            plan.ValueKind == JsonValueKind.String)
            lines.Add($"套餐：{FormatPlan(plan.GetString())}");

        var windowCount = 0;
        if (AddWindow(lines, limits, "primary")) windowCount++;
        if (AddWindow(lines, limits, "secondary")) windowCount++;

        if (windowCount == 0)
            lines.Add("当前账户没有返回可显示的额度窗口，请在 Codex 的 Usage 页面确认。");

        return string.Join(Environment.NewLine, lines);
    }

    private static bool AddWindow(List<string> lines, JsonElement limits, string propertyName)
    {
        if (!limits.TryGetProperty(propertyName, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("usedPercent", out var usedElement))
            return false;

        var used = Math.Clamp(usedElement.GetInt32(), 0, 100);
        var remaining = 100 - used;
        var minutes = window.TryGetProperty("windowDurationMins", out var duration) &&
                      duration.ValueKind == JsonValueKind.Number
            ? duration.GetInt64()
            : 0;
        var label = minutes switch
        {
            300 => "5 小时额度",
            10_080 => "每周额度",
            >= 40_000 and <= 45_000 => "每月额度",
            > 0 => $"{minutes / 60.0:0.#} 小时额度",
            _ => "套餐额度"
        };

        var resetText = string.Empty;
        if (window.TryGetProperty("resetsAt", out var reset) &&
            reset.ValueKind == JsonValueKind.Number)
        {
            var local = DateTimeOffset.FromUnixTimeSeconds(reset.GetInt64()).ToLocalTime();
            resetText = $"，{local:MM月dd日 HH:mm} 重置";
        }

        lines.Add($"{label}：剩余 {remaining}%（已用 {used}%）{resetText}");
        return true;
    }

    private static string FormatPlan(string? plan) => plan?.ToLowerInvariant() switch
    {
        "plus" => "ChatGPT Plus",
        "pro" => "ChatGPT Pro",
        "team" => "ChatGPT Team",
        "business" => "ChatGPT Business",
        "enterprise" => "ChatGPT Enterprise",
        null or "" => "未知",
        _ => plan!
    };
}
