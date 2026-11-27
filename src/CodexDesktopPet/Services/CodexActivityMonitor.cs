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

internal sealed record CodexActivitySnapshot(
    string SessionHash,
    CodexActivityState State,
    string Event,
    DateTime UpdatedAtUtc);

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
    private string _lastSnapshotSignature = string.Empty;

    public CodexActivityMonitor()
    {
        _timer.Tick += (_, _) => Refresh();
    }

    public event EventHandler<CodexActivityState>? StatusChanged;
    public event EventHandler<IReadOnlyList<CodexActivitySnapshot>>? SnapshotsChanged;

    public IReadOnlyList<CodexActivitySnapshot> CurrentSnapshots { get; private set; } = [];

    public static string ActivityDirectory => Path.Combine(AppPaths.DataRoot, "activity");

    public void Start()
    {
        Directory.CreateDirectory(ActivityDirectory);
        Refresh();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        PublishSnapshots([]);
        Publish(CodexActivityState.Idle);
    }

    private void Refresh()
    {
        try
        {
            Directory.CreateDirectory(ActivityDirectory);
            var snapshots = new List<CodexActivitySnapshot>();
            foreach (var path in Directory.EnumerateFiles(ActivityDirectory, "*.json"))
            {
                var snapshot = ReadState(path);
                if (snapshot is not null && DateTime.UtcNow - snapshot.UpdatedAtUtc <= StaleAfter)
                {
                    snapshots.Add(snapshot);
                    continue;
                }

                try { File.Delete(path); }
                catch { }
            }

            var orderedSnapshots = OrderSnapshots(snapshots);

            PublishSnapshots(orderedSnapshots);
            Publish(Aggregate(orderedSnapshots.Select(snapshot => snapshot.State)));
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

    internal static CodexActivitySnapshot[] OrderSnapshots(
        IEnumerable<CodexActivitySnapshot> snapshots) => snapshots
        .OrderBy(snapshot => ActivityPriority(snapshot.State))
        .ThenByDescending(snapshot => snapshot.UpdatedAtUtc)
        .ToArray();

    private static CodexActivitySnapshot? ReadState(string path)
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
            var sessionHash = root.TryGetProperty("sessionHash", out var hashElement)
                ? hashElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(sessionHash))
                sessionHash = Path.GetFileNameWithoutExtension(path);
            var eventName = root.TryGetProperty("event", out var eventElement)
                ? eventElement.GetString() ?? string.Empty
                : string.Empty;
            return new CodexActivitySnapshot(
                sessionHash, state, eventName, updatedAt.ToUniversalTime());
        }
        catch
        {
            return null;
        }
    }

    private void PublishSnapshots(IReadOnlyList<CodexActivitySnapshot> snapshots)
    {
        var signature = string.Join('|', snapshots.Select(snapshot =>
            $"{snapshot.SessionHash}:{snapshot.State}:{snapshot.Event}:{snapshot.UpdatedAtUtc.Ticks}"));
        if (string.Equals(signature, _lastSnapshotSignature, StringComparison.Ordinal))
            return;
        _lastSnapshotSignature = signature;
        CurrentSnapshots = snapshots;
        SnapshotsChanged?.Invoke(this, snapshots);
    }

    private static int ActivityPriority(CodexActivityState state) => state switch
    {
        CodexActivityState.NeedsInput => 0,
        CodexActivityState.Running => 1,
        CodexActivityState.Blocked => 2,
        CodexActivityState.Ready => 3,
        _ => 4
    };

    private void Publish(CodexActivityState state)
    {
        if (_lastState == state)
            return;
        _lastState = state;
        StatusChanged?.Invoke(this, state);
        AppLog.Write($"Aggregated Codex activity changed. State={state}");
    }

    public void Dispose() => _timer.Stop();

}
