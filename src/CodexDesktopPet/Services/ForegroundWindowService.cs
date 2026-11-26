using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexDesktopPet.Services;

internal sealed record ForegroundWindowSnapshot(
    IntPtr Handle,
    int ProcessId,
    string ProcessName,
    string Title)
{
    public string Identity => $"{ProcessId}:{Handle}:{Title}";
}

internal static class ForegroundWindowService
{
    public static ForegroundWindowSnapshot? GetCurrent()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
            return null;

        var length = GetWindowTextLength(handle);
        if (length <= 0)
            return null;
        var titleBuffer = new StringBuilder(length + 1);
        if (GetWindowText(handle, titleBuffer, titleBuffer.Capacity) <= 0)
            return null;
        var title = titleBuffer.ToString().Trim();
        if (string.IsNullOrWhiteSpace(title))
            return null;

        GetWindowThreadProcessId(handle, out var processId);
        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch { processName = "unknown"; }
        return new ForegroundWindowSnapshot(handle, (int)processId, processName, title);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
