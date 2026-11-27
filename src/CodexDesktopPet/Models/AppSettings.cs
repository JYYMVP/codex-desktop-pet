using System.Text.Json;
using System.IO;

namespace CodexDesktopPet.Models;

internal sealed class AppSettings
{
    public string CodexExecutable { get; set; } = "codex.exe";
    public int TimeoutSeconds { get; set; } = 240;
    public int MaximumSelectionCharacters { get; set; } = 12_000;
    public int MaximumAnswerCharacters { get; set; } = 600;
    public string Language { get; set; } = "简体中文";
    public string ReasoningEffort { get; set; } = "none";
    public bool UseFastMode { get; set; }
    public bool OfficialPetsCompanionMode { get; set; }
    public bool PetEnabled { get; set; } = true;
    public bool PetHidden { get; set; }
    public string HotKey { get; set; } = HotKeyGesture.DefaultValue;
    public string OcrHotKey { get; set; } = "Ctrl+Alt+R";
    public string TogglePetHotKey { get; set; } = "Ctrl+Alt+H";
    public string PetPackPath { get; set; } = string.Empty;
    public Dictionary<string, string> PetConversationThreadIds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public bool ScreenWatchingEnabled { get; set; }
    public int ScreenWatchingIntervalMinutes { get; set; } = 10;
    public bool WindowChangeCommentsEnabled { get; set; }
    public int WindowChangeCommentCooldownMinutes { get; set; } = 2;
    public int CommentBubbleSeconds { get; set; } = 14;
    public bool FollowCodexActivity { get; set; } = true;
    public bool BindLifetimeToCodex { get; set; } = true;
    public int IdleActivityLevel { get; set; } = 1;
    public bool ReduceMotion { get; set; }
    public bool FollowPointerWithPet { get; set; } = true;
    public bool StayOnTop { get; set; } = true;

    public static string SettingsPath => Path.Combine(AppPaths.DataRoot, "settings.json");

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
            }
            if (!HotKeyGesture.TryParse(loaded.HotKey, out var hotKey, out _))
                loaded.HotKey = HotKeyGesture.DefaultValue;
            else
                loaded.HotKey = hotKey.DisplayName;
            if (!HotKeyGesture.TryParse(loaded.OcrHotKey, out var ocrHotKey, out _) ||
                string.Equals(ocrHotKey.DisplayName, loaded.HotKey,
                    StringComparison.OrdinalIgnoreCase))
                loaded.OcrHotKey = "Ctrl+Alt+R";
            else
                loaded.OcrHotKey = ocrHotKey.DisplayName;
            if (!HotKeyGesture.TryParse(loaded.TogglePetHotKey, out var toggleHotKey, out _) ||
                string.Equals(toggleHotKey.DisplayName, loaded.HotKey,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(toggleHotKey.DisplayName, loaded.OcrHotKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                loaded.TogglePetHotKey = new[] { "Ctrl+Alt+H", "Ctrl+Shift+H", "Ctrl+Alt+Space" }
                    .First(candidate =>
                        !string.Equals(candidate, loaded.HotKey, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(candidate, loaded.OcrHotKey, StringComparison.OrdinalIgnoreCase));
            }
            else
                loaded.TogglePetHotKey = toggleHotKey.DisplayName;
            // `minimal` is rejected by several Codex models (including
            // gpt-5.6-sol) after expensive transport retries. `none` is the
            // model-supported no-reasoning fast path.
            if (string.Equals(loaded.ReasoningEffort, "minimal", StringComparison.OrdinalIgnoreCase))
                loaded.ReasoningEffort = "none";
            loaded.PetConversationThreadIds ??= new(StringComparer.OrdinalIgnoreCase);
            loaded.ScreenWatchingIntervalMinutes = Math.Clamp(
                loaded.ScreenWatchingIntervalMinutes, 1, 240);
            loaded.WindowChangeCommentCooldownMinutes = Math.Clamp(
                loaded.WindowChangeCommentCooldownMinutes, 1, 60);
            loaded.CommentBubbleSeconds = Math.Clamp(loaded.CommentBubbleSeconds, 4, 60);
            loaded.IdleActivityLevel = Math.Clamp(loaded.IdleActivityLevel, 0, 2);
            loaded.Save();
            return loaded;
        }
        catch (Exception ex)
        {
            var backupPath = $"{SettingsPath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            try
            {
                File.Copy(SettingsPath, backupPath, false);
                Services.AppLog.Write(
                    $"Settings were invalid and preserved at {backupPath}: {ex.Message}");
            }
            catch (Exception backupException)
            {
                Services.AppLog.Write(
                    $"Settings were invalid and could not be backed up: {backupException.Message}");
            }

            var defaults = new AppSettings();
            defaults.Save();
            return defaults;
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
