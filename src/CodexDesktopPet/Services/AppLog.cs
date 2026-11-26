using System.IO;

namespace CodexDesktopPet.Services;

internal static class AppLog
{
    private const long MaximumLogBytes = 4 * 1024 * 1024;
    private static readonly object Sync = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexDesktopPet", "desktop-pet.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            lock (Sync)
            {
                RotateIfNeeded();
                File.AppendAllText(LogPath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never stop the assistant.
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaximumLogBytes)
            return;

        var previousLogPath = LogPath + ".1";
        File.Move(LogPath, previousLogPath, overwrite: true);
    }
}
