using System.Windows;
using System.Windows.Input;
using CodexDesktopPet.Models;

namespace CodexDesktopPet;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    internal SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        CodexExecutableBox.Text = settings.CodexExecutable;
        LanguageBox.Text = settings.Language;
        HotKeyBox.Text = HotKeyGesture.ParseOrDefault(settings.HotKey).DisplayName;
        OcrHotKeyBox.Text = HotKeyGesture.ParseOrDefault(settings.OcrHotKey).DisplayName;
        TogglePetHotKeyBox.Text = HotKeyGesture.ParseOrDefault(settings.TogglePetHotKey).DisplayName;
        ReasoningEffortBox.SelectedValue = settings.ReasoningEffort;
        FastModeBox.IsChecked = settings.UseFastMode;
        OfficialPetsModeBox.IsChecked = settings.OfficialPetsCompanionMode;
        PetHiddenBox.IsChecked = settings.PetHidden;
        FollowCodexActivityBox.IsChecked = settings.FollowCodexActivity;
        ScreenWatchingBox.IsChecked = settings.ScreenWatchingEnabled;
        ScreenIntervalBox.Text = settings.ScreenWatchingIntervalMinutes.ToString();
        WindowChangeCommentsBox.IsChecked = settings.WindowChangeCommentsEnabled;
        WindowChangeCooldownBox.Text = settings.WindowChangeCommentCooldownMinutes.ToString();
        ContextTurnsBox.Text = settings.ContextTurns.ToString();
        MaximumAnswerBox.Text = settings.MaximumAnswerCharacters.ToString();
        TimeoutBox.Text = settings.TimeoutSeconds.ToString();
        PetPackPathBox.Text = settings.PetPackPath;
        StayOnTopBox.IsChecked = settings.StayOnTop;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ContextTurnsBox.Text, out var turns) || turns is < 0 or > 12 ||
            !int.TryParse(MaximumAnswerBox.Text, out var answerLength) || answerLength is < 100 or > 6000 ||
            !int.TryParse(TimeoutBox.Text, out var timeout) || timeout is < 15 or > 600 ||
            !int.TryParse(ScreenIntervalBox.Text, out var screenInterval) || screenInterval is < 1 or > 240 ||
            !int.TryParse(WindowChangeCooldownBox.Text, out var windowCooldown) || windowCooldown is < 1 or > 60 ||
            string.IsNullOrWhiteSpace(CodexExecutableBox.Text) ||
            string.IsNullOrWhiteSpace(LanguageBox.Text) ||
            ReasoningEffortBox.SelectedValue is not string reasoning)
        {
            ValidationText.Text = "请检查输入范围。";
            return;
        }

        if (!HotKeyGesture.TryParse(HotKeyBox.Text, out var hotKey, out var hotKeyError))
        {
            ValidationText.Text = hotKeyError;
            return;
        }

        if (!HotKeyGesture.TryParse(OcrHotKeyBox.Text, out var ocrHotKey, out var ocrHotKeyError))
        {
            ValidationText.Text = ocrHotKeyError;
            return;
        }
        if (!HotKeyGesture.TryParse(TogglePetHotKeyBox.Text, out var togglePetHotKey,
                out var toggleHotKeyError))
        {
            ValidationText.Text = toggleHotKeyError;
            return;
        }
        var distinctHotKeys = new[]
        {
            hotKey.DisplayName, ocrHotKey.DisplayName, togglePetHotKey.DisplayName
        }.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3;
        if (!distinctHotKeys)
        {
            ValidationText.Text = "划词提问、OCR 框选和显示/隐藏必须使用三个不同快捷键。";
            return;
        }

        _settings.CodexExecutable = CodexExecutableBox.Text.Trim();
        _settings.Language = LanguageBox.Text.Trim();
        _settings.HotKey = hotKey.DisplayName;
        _settings.OcrHotKey = ocrHotKey.DisplayName;
        _settings.TogglePetHotKey = togglePetHotKey.DisplayName;
        _settings.ReasoningEffort = reasoning;
        _settings.UseFastMode = FastModeBox.IsChecked == true;
        _settings.OfficialPetsCompanionMode = OfficialPetsModeBox.IsChecked == true;
        _settings.PetHidden = PetHiddenBox.IsChecked == true;
        _settings.FollowCodexActivity = FollowCodexActivityBox.IsChecked == true;
        _settings.ScreenWatchingEnabled = ScreenWatchingBox.IsChecked == true;
        _settings.ScreenWatchingIntervalMinutes = screenInterval;
        _settings.WindowChangeCommentsEnabled = WindowChangeCommentsBox.IsChecked == true;
        _settings.WindowChangeCommentCooldownMinutes = windowCooldown;
        _settings.ContextTurns = turns;
        _settings.MaximumAnswerCharacters = answerLength;
        _settings.TimeoutSeconds = timeout;
        _settings.PetPackPath = PetPackPathBox.Text.Trim();
        _settings.StayOnTop = StayOnTopBox.IsChecked == true;
        _settings.Save();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void HotKeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        => CaptureHotKey(e, HotKeyBox);

    private void OcrHotKeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        => CaptureHotKey(e, OcrHotKeyBox);

    private void TogglePetHotKeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        => CaptureHotKey(e, TogglePetHotKeyBox);

    private void CaptureHotKey(System.Windows.Input.KeyEventArgs e, System.Windows.Controls.TextBox target)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HotKeyGesture.IsModifierKey(key))
        {
            ValidationText.Text = "继续按下一个普通按键。";
            return;
        }

        if (!HotKeyGesture.TryCreate(Keyboard.Modifiers, key,
                out var gesture, out var error))
        {
            ValidationText.Text = error;
            return;
        }

        target.Text = gesture.DisplayName;
        ValidationText.Text = string.Empty;
    }
}
