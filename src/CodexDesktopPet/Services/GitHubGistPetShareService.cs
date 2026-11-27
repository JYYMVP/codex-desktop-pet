using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexDesktopPet.Models;

namespace CodexDesktopPet.Services;

internal sealed partial class GitHubGistPetShareService
{
    private const long MaximumArchiveBytes = 7L * 1024 * 1024;
    private const int MaximumBase64Bytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static readonly HttpClient Http = CreateHttpClient();
    private readonly string _recordsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GitHubGistPetShareService() : this(Path.Combine(
        AppPaths.DataRoot, "shares.json"))
    {
    }

    internal GitHubGistPetShareService(string recordsPath)
    {
        _recordsPath = Path.GetFullPath(recordsPath);
    }

    public IReadOnlyList<PetShareRecord> ListShares()
    {
        try
        {
            if (!File.Exists(_recordsPath))
                return [];
            return JsonSerializer.Deserialize<List<PetShareRecord>>(
                       File.ReadAllText(_recordsPath), JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            AppLog.Write($"Pet share records could not be read: {ex.Message}");
            return [];
        }
    }

    public PetShareRecord? FindShare(string petId) => ListShares().FirstOrDefault(record =>
        string.Equals(record.PetId, petId, StringComparison.OrdinalIgnoreCase));

    public async Task<PetShareRecord> ShareAsync(
        PetLibraryService library,
        PetLibraryItem pet,
        CancellationToken cancellationToken = default)
    {
        if (pet.IsBuiltIn)
            throw new InvalidOperationException("内置宠物不能公开分享。");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (FindShare(pet.Id) is { } existing)
                throw new InvalidOperationException($"这个宠物已经公开分享：{existing.Url}");

            var temporaryRoot = CreateTemporaryRoot();
            try
            {
                var archiveName = $"{pet.Id}.codexpet";
                var archivePath = Path.Combine(temporaryRoot, archiveName);
                library.Export(pet, archivePath);
                var archive = await File.ReadAllBytesAsync(archivePath, cancellationToken);
                if (archive.LongLength > MaximumArchiveBytes)
                    throw new InvalidDataException(
                        "公网分享包不能超过 7 MiB；请减少动画帧，或继续使用本地 .codexpet 文件分享。");

                var base64Name = archiveName + ".b64";
                var base64Path = Path.Combine(temporaryRoot, base64Name);
                await File.WriteAllTextAsync(base64Path,
                    Convert.ToBase64String(archive, Base64FormattingOptions.InsertLineBreaks),
                    new UTF8Encoding(false), cancellationToken);

                var metadata = new ShareMetadata(
                    "codex-desktop-pet-share-v1",
                    pet.Id,
                    pet.Name,
                    pet.Description,
                    base64Name,
                    Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant(),
                    DateTime.UtcNow);
                var metadataPath = Path.Combine(temporaryRoot, "pet-share.json");
                await File.WriteAllTextAsync(metadataPath,
                    JsonSerializer.Serialize(metadata, JsonOptions),
                    new UTF8Encoding(false), cancellationToken);

                var output = await RunGhAsync(
                [
                    "gist", "create", "--public", "--desc",
                    $"Codex Desktop Pet: {SanitizeDescription(pet.Name)}",
                    metadataPath, base64Path
                ], cancellationToken);
                var url = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .LastOrDefault(line => Uri.TryCreate(line, UriKind.Absolute, out var uri) &&
                                           uri.Host.Equals("gist.github.com",
                                               StringComparison.OrdinalIgnoreCase))
                          ?? throw new InvalidDataException("GitHub CLI 没有返回有效的 Gist 链接。");
                if (!TryParseGistId(url, out var gistId))
                    throw new InvalidDataException("无法从 GitHub Gist 链接中读取分享 ID。");

                var record = new PetShareRecord(pet.Id, pet.Name, gistId, url, DateTime.UtcNow);
                var records = ListShares().ToList();
                records.Add(record);
                WriteRecords(records);
                AppLog.Write($"Created public pet share. Pet={pet.Id}; Gist={gistId}");
                return record;
            }
            finally
            {
                DeleteTemporaryRoot(temporaryRoot);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UnshareAsync(
        PetShareRecord record,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await RunGhAsync(["gist", "delete", record.GistId, "--yes"], cancellationToken);
            var records = ListShares().Where(candidate =>
                !string.Equals(candidate.GistId, record.GistId,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            WriteRecords(records);
            AppLog.Write($"Revoked public pet share. Pet={record.PetId}; Gist={record.GistId}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PetLibraryItem> AdoptAsync(
        PetLibraryService library,
        string shareUrlOrId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseGistId(shareUrlOrId, out var gistId))
            throw new InvalidDataException("请输入完整的 GitHub Gist 分享链接。");

        var apiUrl = new Uri($"https://api.github.com/gists/{gistId}");
        using var response = await Http.GetAsync(apiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var gist = await JsonDocument.ParseAsync(responseStream,
            cancellationToken: cancellationToken);
        var files = gist.RootElement.GetProperty("files");
        if (!files.TryGetProperty("pet-share.json", out var metadataFile))
            throw new InvalidDataException("这个 Gist 不是 Codex Desktop Pet 分享链接。");

        var metadataJson = await DownloadRawTextAsync(metadataFile, cancellationToken);
        var metadata = JsonSerializer.Deserialize<ShareMetadata>(metadataJson, JsonOptions)
                       ?? throw new InvalidDataException("宠物分享元数据无效。");
        if (!string.Equals(metadata.Schema, "codex-desktop-pet-share-v1",
                StringComparison.Ordinal))
            throw new InvalidDataException("不支持这个宠物分享版本。");
        if (!files.TryGetProperty(metadata.ArchiveFile, out var archiveFile))
            throw new InvalidDataException("宠物分享中缺少归档内容。");

        var base64 = await DownloadRawTextAsync(archiveFile, cancellationToken);
        var archive = DecodeArchive(base64, metadata.Sha256);
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var archivePath = Path.Combine(temporaryRoot, "adopted.codexpet");
            await File.WriteAllBytesAsync(archivePath, archive, cancellationToken);
            var adopted = library.Import(archivePath);
            AppLog.Write($"Adopted public pet share. Pet={adopted.Id}; Gist={gistId}");
            return adopted;
        }
        finally
        {
            DeleteTemporaryRoot(temporaryRoot);
        }
    }

    internal static bool TryParseGistId(string? value, out string gistId)
    {
        gistId = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var trimmed = value.Trim();
        if (GistIdRegex().IsMatch(trimmed))
        {
            gistId = trimmed.ToLowerInvariant();
            return true;
        }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("gist.github.com", StringComparison.OrdinalIgnoreCase))
            return false;
        var candidate = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();
        if (candidate is null || !GistIdRegex().IsMatch(candidate))
            return false;
        gistId = candidate.ToLowerInvariant();
        return true;
    }

    internal static byte[] DecodeArchive(string base64, string expectedSha256)
    {
        if (Encoding.UTF8.GetByteCount(base64) > MaximumBase64Bytes)
            throw new InvalidDataException("宠物分享内容过大。");
        byte[] archive;
        try { archive = Convert.FromBase64String(base64); }
        catch (FormatException ex) { throw new InvalidDataException("宠物分享内容不是有效的 Base64。", ex); }
        if (archive.LongLength > MaximumArchiveBytes)
            throw new InvalidDataException("宠物分享归档过大。");
        var actual = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("宠物分享校验失败，内容可能已损坏或被修改。");
        return archive;
    }

    private static async Task<string> DownloadRawTextAsync(
        JsonElement file,
        CancellationToken cancellationToken)
    {
        var rawUrl = file.GetProperty("raw_url").GetString();
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("gist.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("宠物分享包含不受信任的下载地址。");
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumBase64Bytes)
            throw new InvalidDataException("宠物分享文件过大。");
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (Encoding.UTF8.GetByteCount(content) > MaximumBase64Bytes)
            throw new InvalidDataException("宠物分享文件过大。");
        return content;
    }

    private async Task<string> RunGhAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveGhExecutable(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("无法启动 GitHub CLI。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("GitHub 分享操作超时，请检查网络后重试。");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? "GitHub CLI 操作失败。请先运行 gh auth login。"
                : stderr.Trim());
        return stdout.Trim();
    }

    private void WriteRecords(IEnumerable<PetShareRecord> records)
    {
        var directory = Path.GetDirectoryName(_recordsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _recordsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(records, JsonOptions),
            new UTF8Encoding(false));
        File.Move(temporaryPath, _recordsPath, overwrite: true);
    }

    private static string ResolveGhExecutable()
    {
        var wingetLink = Path.Combine(
            AppPaths.LocalApplicationData,
            "Microsoft", "WinGet", "Links", "gh.exe");
        return File.Exists(wingetLink) ? wingetLink : "gh.exe";
    }

    private static string SanitizeDescription(string value)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized[..Math.Min(normalized.Length, 80)];
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexDesktopPet", "share-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root);
            var allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodexDesktopPet")) +
                              Path.DirectorySeparatorChar;
            if (fullRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(fullRoot))
                Directory.Delete(fullRoot, recursive: true);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Temporary pet share cleanup failed: {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexDesktopPet/0.10");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    [GeneratedRegex("^[a-fA-F0-9]{20,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex GistIdRegex();

    private sealed record ShareMetadata(
        string Schema,
        string PetId,
        string Name,
        string Description,
        string ArchiveFile,
        string Sha256,
        DateTime CreatedAtUtc);
}
