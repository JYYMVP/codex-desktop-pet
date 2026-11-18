using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CodexDesktopPet.Models;
using CodexDesktopPet.Services;
using Forms = System.Windows.Forms;

namespace CodexDesktopPet;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly SelectionCaptureService _selectionCapture = new();
    private readonly CodexCliService _codex;
    private readonly CodexUsageService _usage;
    private readonly PixelPetController _petController;
    private readonly List<ConversationTurn> _conversation = [];
    private GlobalHotKeyManager? _hotKey;
    private Forms.NotifyIcon? _trayIcon;
    private CancellationTokenSource? _requestCancellation;
    private bool _allowClose;
    private bool _busy;
    private System.Windows.Point? _petPressPosition;
    private bool _petDragged;

    public MainWindow()
    {
        InitializeComponent();
        _codex = new CodexCliService(_settings);
        _usage = new CodexUsageService(_settings);
        _petController = new PixelPetController(PetImage, _settings.PetPackPath);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppLog.Write($"Window loaded. Version={Environment.Version}; Settings={AppSettings.SettingsPath}");
        Left = SystemParameters.WorkArea.Right - ActualWidth - 18;
        Top = SystemParameters.WorkArea.Bottom - ActualHeight - 18;
        Topmost = _settings.StayOnTop;
        UpdateContextHint();

        CreateTrayIcon();
        SetPetState(PetState.Idle);
        StartIdleAnimation();

        try
        {
            _hotKey = new GlobalHotKeyManager(this,
                ModifierKeys.Control | ModifierKeys.Alt, Key.Q);
            _hotKey.Pressed += HotKey_Pressed;
            AppLog.Write("Global hotkey Ctrl+Alt+Q registered successfully.");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Hotkey registration failed: {ex}");
            ShowMessage(ex.Message, PetState.Error);
        }
    }

    private async void HotKey_Pressed(object? sender, EventArgs e)
    {
        AppLog.Write($"Hotkey received. Busy={_busy}");
        if (_busy)
            return;

        _busy = true;
        if (!IsVisible)
            Show();
        AnswerPanel.Visibility = Visibility.Collapsed;
        SetPetState(PetState.Capturing);

        try
        {
            var selectedText = await _selectionCapture.CaptureAsync();
            AppLog.Write($"Selection captured. Length={selectedText?.Length ?? 0}");
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                ShowMessage("没有读到选中的文字。请先选中一段文字，再按 Ctrl+Alt+Q。",
                    PetState.NeedsInput);
                return;
            }

            await AskAndRecordAsync(selectedText, isSelection: true);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Request failed: {ex}");
            ShowMessage(FriendlyError(ex), PetState.Error);
        }
        finally
        {
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            _busy = false;
        }
    }

    private void ShowMessage(string message, PetState state)
    {
        AnswerText.Text = message;
        AnswerPanel.Visibility = Visibility.Visible;
        SetPetState(state);
    }

    private void SetPetState(PetState state)
    {
        StopThinkingAnimation();
        var (status, color) = state switch
        {
            PetState.Capturing => ("正在读取选区…", System.Windows.Media.Color.FromRgb(139, 227, 255)),
            PetState.Thinking => ("Codex 正在思考…", System.Windows.Media.Color.FromRgb(187, 139, 255)),
            PetState.Ready => ("回答好啦", System.Windows.Media.Color.FromRgb(104, 233, 255)),
            PetState.NeedsInput => ("先选中一些文字吧", System.Windows.Media.Color.FromRgb(255, 210, 112)),
            PetState.Error => ("遇到了一点问题", System.Windows.Media.Color.FromRgb(255, 122, 146)),
            _ => ("选中文字后按 Ctrl+Alt+Q", System.Windows.Media.Color.FromRgb(104, 233, 255))
        };

        StatusText.Text = status;
        GlowRing.Stroke = new SolidColorBrush(color) { Opacity = 0.65 };
        _petController.SetState(state);
        if (state == PetState.Thinking)
            StartThinkingAnimation();
    }

    private void StartIdleAnimation()
    {
        PetTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(0, -7, TimeSpan.FromSeconds(1.5))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
    }

    private void StartThinkingAnimation()
    {
        var pulse = new DoubleAnimation(1, 1.08, TimeSpan.FromMilliseconds(450))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        PetScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
        PetScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse);
    }

    private void StopThinkingAnimation()
    {
        PetScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        PetScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        PetScale.ScaleX = PetScale.ScaleY = 1;
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示桌宠", null, (_, _) => ShowFromTray());
        menu.Items.Add("查看 Codex 套餐额度", null,
            async (_, _) => await ShowCodexUsageAsync());
        menu.Items.Add("清空对话上下文", null, (_, _) => ClearContext());
        menu.Items.Add("设置…", null, (_, _) => OpenSettings());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Codex 桌宠 — Ctrl+Alt+Q",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Topmost = false;
        Topmost = true;
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private static string FriendlyError(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("not recognized", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("找不到 Codex CLI", StringComparison.OrdinalIgnoreCase))
            return "没有找到 Codex CLI。请打开设置文件并填写 CodexExecutable 的完整路径。";
        return message;
    }

    private void CopyAnswer_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(AnswerText.Text))
            System.Windows.Clipboard.SetText(AnswerText.Text);
    }

    private void HideAnswer_Click(object sender, RoutedEventArgs e) =>
        AnswerPanel.Visibility = Visibility.Collapsed;

    private async void ShowUsage_Click(object sender, RoutedEventArgs e) =>
        await ShowCodexUsageAsync();

    private async Task ShowCodexUsageAsync()
    {
        if (_busy)
            return;

        _busy = true;
        AnswerPanel.Visibility = Visibility.Visible;
        AnswerText.Text = "正在查询 Codex 套餐额度…";
        SetPetState(PetState.Thinking);
        AppLog.Write("Starting Codex plan usage query.");

        try
        {
            _requestCancellation = new CancellationTokenSource();
            var summary = await _usage.GetPlanSummaryAsync(_requestCancellation.Token);
            AppLog.Write("Codex plan usage query completed.");
            ShowMessage(summary, PetState.Ready);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Codex plan usage query failed: {ex}");
            ShowMessage(FriendlyError(ex), PetState.Error);
        }
        finally
        {
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            _busy = false;
        }
    }

    private void ToggleAnswer_Click(object sender, RoutedEventArgs e) =>
        AnswerPanel.Visibility = AnswerPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void ExitMenu_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private async void AskDirect_Click(object sender, RoutedEventArgs e) =>
        await AskDirectAsync();

    private async void DirectQuestion_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;
        e.Handled = true;
        await AskDirectAsync();
    }

    private async Task AskDirectAsync()
    {
        var question = DirectQuestionBox.Text.Trim();
        if (_busy || string.IsNullOrWhiteSpace(question))
            return;

        _busy = true;
        AnswerPanel.Visibility = Visibility.Visible;
        DirectQuestionBox.Clear();
        try
        {
            await AskAndRecordAsync(question, isSelection: false);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Direct question failed: {ex}");
            ShowMessage(FriendlyError(ex), PetState.Error);
        }
        finally
        {
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            _busy = false;
        }
    }

    private async Task AskAndRecordAsync(string input, bool isSelection)
    {
        SetPetState(PetState.Thinking);
        _requestCancellation = new CancellationTokenSource();
        AppLog.Write($"Starting Codex request. Mode={(isSelection ? "selection" : "direct")}; ContextTurns={_conversation.Count}");
        var answer = await _codex.AskAsync(input, _conversation, isSelection,
            _requestCancellation.Token);
        AppLog.Write($"Codex request completed. AnswerLength={answer.Length}");

        if (_settings.ContextTurns > 0)
        {
            var storedInput = input.Length > _settings.MaximumSelectionCharacters
                ? input[.._settings.MaximumSelectionCharacters]
                : input;
            _conversation.Add(new ConversationTurn(
                isSelection ? $"[所选文字]\n{storedInput}" : storedInput, answer));
            while (_conversation.Count > _settings.ContextTurns)
                _conversation.RemoveAt(0);
        }

        UpdateContextHint();
        ShowMessage(answer, PetState.Ready);
    }

    private void UpdateContextHint() =>
        HintText.Text = $"{_conversation.Count} 轮上下文";

    private void ClearContext_Click(object sender, RoutedEventArgs e) => ClearContext();

    private void ClearContext()
    {
        _conversation.Clear();
        UpdateContextHint();
        ShowMessage("对话上下文已清空。下一次提问会从全新会话开始。", PetState.Idle);
        AppLog.Write("Conversation context cleared.");
    }

    private void FocusQuestion_Click(object sender, RoutedEventArgs e)
    {
        AnswerPanel.Visibility = Visibility.Visible;
        Activate();
        DirectQuestionBox.Focus();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        var wasTopmost = Topmost;
        Topmost = false;
        var dialog = new SettingsWindow(_settings) { Owner = this, Topmost = true };
        if (dialog.ShowDialog() == true)
        {
            Topmost = _settings.StayOnTop;
            _petController.Reload(_settings.PetPackPath);
            while (_conversation.Count > _settings.ContextTurns)
                _conversation.RemoveAt(0);
            UpdateContextHint();
            ShowMessage("设置已保存并应用。", PetState.Ready);
            AppLog.Write("Settings saved and applied.");
        }
        else
        {
            Topmost = wasTopmost;
        }
    }

    private void PetBody_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _petPressPosition = e.GetPosition(this);
        _petDragged = false;
        PetBody.CaptureMouse();
        e.Handled = true;
    }

    private void PetBody_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_petPressPosition is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(this);
        var movedFarEnough =
            Math.Abs(current.X - _petPressPosition.Value.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(current.Y - _petPressPosition.Value.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (!movedFarEnough)
            return;

        _petDragged = true;
        _petPressPosition = null;
        PetBody.ReleaseMouseCapture();
        try { DragMove(); }
        catch (InvalidOperationException) { }
        e.Handled = true;
    }

    private void PetBody_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        PetBody.ReleaseMouseCapture();
        if (_petPressPosition is not null && !_petDragged)
        {
            AnswerPanel.Visibility = AnswerPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            AppLog.Write($"Pet clicked. AnswerVisible={AnswerPanel.Visibility == Visibility.Visible}");
        }

        _petPressPosition = null;
        _petDragged = false;
        e.Handled = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        AppLog.Write("Application closed.");
        _requestCancellation?.Cancel();
        _hotKey?.Dispose();
        _trayIcon?.Dispose();
        _petController.Dispose();
    }
}
