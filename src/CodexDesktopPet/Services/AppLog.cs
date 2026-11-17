using System.IO;

namespace CodexDesktopPet.Services;

internal static class AppLog
{
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
                File.AppendAllText(LogPath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never stop the assistant.
        }
    }
}
