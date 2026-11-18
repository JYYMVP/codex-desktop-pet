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
        ReasoningEffortBox.SelectedValue = settings.ReasoningEffort;
        FastModeBox.IsChecked = settings.UseFastMode;
        ScreenWatchingBox.IsChecked = settings.ScreenWatchingEnabled;
        ScreenIntervalBox.Text = settings.ScreenWatchingIntervalMinutes.ToString();
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

        _settings.CodexExecutable = CodexExecutableBox.Text.Trim();
        _settings.Language = LanguageBox.Text.Trim();
        _settings.HotKey = hotKey.DisplayName;
        _settings.ReasoningEffort = reasoning;
        _settings.UseFastMode = FastModeBox.IsChecked == true;
        _settings.ScreenWatchingEnabled = ScreenWatchingBox.IsChecked == true;
        _settings.ScreenWatchingIntervalMinutes = screenInterval;
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

        HotKeyBox.Text = gesture.DisplayName;
        ValidationText.Text = string.Empty;
    }
}
