using System.Text.Json;
using System.IO;

namespace CodexDesktopPet.Models;

internal sealed class AppSettings
{
    public string CodexExecutable { get; init; } = "codex.exe";
    public int TimeoutSeconds { get; init; } = 120;
    public int MaximumSelectionCharacters { get; init; } = 12_000;
    public int MaximumAnswerCharacters { get; init; } = 600;
    public string Language { get; init; } = "简体中文";

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexDesktopPet", "settings.json");

    public static AppSettings Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        if (!File.Exists(SettingsPath))
        {
            var defaults = new AppSettings();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(defaults, JsonOptions));
            return defaults;
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
