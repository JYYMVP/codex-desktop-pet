using System.Windows;
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
        ReasoningEffortBox.SelectedValue = settings.ReasoningEffort;
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
            string.IsNullOrWhiteSpace(CodexExecutableBox.Text) ||
            string.IsNullOrWhiteSpace(LanguageBox.Text) ||
            ReasoningEffortBox.SelectedValue is not string reasoning)
        {
            ValidationText.Text = "请检查输入范围。";
            return;
        }

        _settings.CodexExecutable = CodexExecutableBox.Text.Trim();
        _settings.Language = LanguageBox.Text.Trim();
        _settings.ReasoningEffort = reasoning;
        _settings.ContextTurns = turns;
        _settings.MaximumAnswerCharacters = answerLength;
        _settings.TimeoutSeconds = timeout;
        _settings.PetPackPath = PetPackPathBox.Text.Trim();
        _settings.StayOnTop = StayOnTopBox.IsChecked == true;
        _settings.Save();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
