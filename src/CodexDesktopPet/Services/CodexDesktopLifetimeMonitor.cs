using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

namespace CodexDesktopPet.Services;

/// <summary>
/// Keeps the helper's lifetime aligned with the trusted Codex desktop package.
/// Codex CLI/app-server child processes are deliberately ignored.
/// </summary>
internal sealed class CodexDesktopLifetimeMonitor : IDisposable
{
    private const int MissingObservationsBeforeExit = 3;
    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromSeconds(8);
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };
    private DateTime _graceUntilUtc;
    private int _missingObservations;

    public CodexDesktopLifetimeMonitor()
    {
        _timer.Tick += (_, _) => CheckHost();
    }

    public event EventHandler? CodexExited;

    public void Start()
    {
        _missingObservations = 0;
        _graceUntilUtc = DateTime.UtcNow + StartupGracePeriod;
        _timer.Start();
        CheckHost();
        AppLog.Write("Codex-bound lifetime monitoring started.");
    }

    public void Stop()
    {
        _timer.Stop();
        _missingObservations = 0;
    }

    internal static bool IsCodexDesktopRunning()
    {
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) &&
                        IsTrustedCodexDesktopExecutablePath(path))
                        return true;
                }
                catch
                {
                    // An unreadable same-name process is not trusted as the Codex host.
                }
            }
        }
        return false;
    }

    internal static bool IsTrustedCodexDesktopExecutablePath(string executablePath)
    {
        try
        {
            var normalized = Path.GetFullPath(executablePath).Replace('/', '\\');
            return normalized.EndsWith("\\app\\ChatGPT.exe",
                       StringComparison.OrdinalIgnoreCase) &&
                   normalized.Contains("\\WindowsApps\\OpenAI.Codex_",
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static bool ShouldExitAfterObservation(
        int previousMissingObservations,
        bool hostRunning,
        out int nextMissingObservations)
    {
        nextMissingObservations = hostRunning ? 0 : previousMissingObservations + 1;
        return nextMissingObservations >= MissingObservationsBeforeExit;
    }

    private void CheckHost()
    {
        if (IsCodexDesktopRunning())
        {
            _missingObservations = 0;
            return;
        }
        if (DateTime.UtcNow < _graceUntilUtc)
            return;

        if (!ShouldExitAfterObservation(_missingObservations, hostRunning: false,
                out _missingObservations))
            return;

        _timer.Stop();
        AppLog.Write("Trusted Codex desktop process is no longer running; closing desktop pet.");
        CodexExited?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stop();
}
