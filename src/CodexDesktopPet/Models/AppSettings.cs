using System.Text.Json;
using System.IO;

namespace CodexDesktopPet.Models;

internal sealed class AppSettings
{
    public string CodexExecutable { get; set; } = "codex.exe";
    public int TimeoutSeconds { get; set; } = 240;
    public int MaximumSelectionCharacters { get; set; } = 12_000;
    public int MaximumAnswerCharacters { get; set; } = 600;
    public int ContextTurns { get; set; } = 4;
    public string Language { get; set; } = "简体中文";
    public string PetPackPath { get; set; } = string.Empty;
    public bool StayOnTop { get; set; } = true;

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
            var loaded = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
            // 120 seconds was the original default, but real Codex Desktop
            // responses on Windows can legitimately take a little longer.
            if (loaded.TimeoutSeconds == 120)
            {
                loaded.TimeoutSeconds = 240;
                loaded.Save();
            }
            return loaded;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
