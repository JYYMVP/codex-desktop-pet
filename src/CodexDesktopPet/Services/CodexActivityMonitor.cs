using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace CodexDesktopPet.Services;

internal enum CodexActivityState
{
    Idle,
    Running,
    NeedsInput,
    Ready,
    Blocked
}

/// <summary>
/// Aggregates the small per-session status files written by the plugin's
/// lifecycle hooks. It deliberately reads only status metadata; it never reads
/// prompts, model output, or transcript contents.
/// </summary>
internal sealed class CodexActivityMonitor : IDisposable
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(700)
    };
    private CodexActivityState _lastState = CodexActivityState.Idle;

    public CodexActivityMonitor()
    {
        _timer.Tick += (_, _) => Refresh();
    }

    public event EventHandler<CodexActivityState>? StatusChanged;

    public static string ActivityDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexDesktopPet", "activity");

    public void Start()
    {
        Directory.CreateDirectory(ActivityDirectory);
        Refresh();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Publish(CodexActivityState.Idle);
    }

    private void Refresh()
    {
        try
        {
            Directory.CreateDirectory(ActivityDirectory);
            var states = Directory.EnumerateFiles(ActivityDirectory, "*.json")
                .Select(ReadState)
                .Where(snapshot => snapshot is not null)
                .Cast<ActivitySnapshot>()
                .Where(snapshot => DateTime.UtcNow - snapshot.UpdatedAtUtc <= StaleAfter)
                .Select(snapshot => snapshot.State)
                .ToArray();

            Publish(Aggregate(states));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Codex activity refresh failed: {ex.Message}");
        }
    }

    internal static CodexActivityState Aggregate(IEnumerable<CodexActivityState> states)
    {
        var available = states.ToHashSet();
        return available.Contains(CodexActivityState.NeedsInput)
            ? CodexActivityState.NeedsInput
            : available.Contains(CodexActivityState.Running)
                ? CodexActivityState.Running
                : available.Contains(CodexActivityState.Blocked)
                    ? CodexActivityState.Blocked
                    : available.Contains(CodexActivityState.Ready)
                        ? CodexActivityState.Ready
                        : CodexActivityState.Idle;
    }

    private static ActivitySnapshot? ReadState(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("state", out var stateElement) ||
                !Enum.TryParse<CodexActivityState>(stateElement.GetString(), true, out var state))
                return null;
            if (!root.TryGetProperty("updatedAtUtc", out var updatedElement) ||
                !DateTime.TryParse(updatedElement.GetString(), out var updatedAt))
                return null;
            return new ActivitySnapshot(state, updatedAt.ToUniversalTime());
        }
        catch
        {
            return null;
        }
    }

    private void Publish(CodexActivityState state)
    {
        if (_lastState == state)
            return;
        _lastState = state;
        StatusChanged?.Invoke(this, state);
        AppLog.Write($"Aggregated Codex activity changed. State={state}");
    }

    public void Dispose() => _timer.Stop();

    private sealed record ActivitySnapshot(CodexActivityState State, DateTime UpdatedAtUtc);
}
