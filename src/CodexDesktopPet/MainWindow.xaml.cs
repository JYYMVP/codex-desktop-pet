using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CodexDesktopPet.Models;
using CodexDesktopPet.Services;
using Forms = System.Windows.Forms;

namespace CodexDesktopPet;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly SelectionCaptureService _selectionCapture = new();
    private readonly CodexAppServerService _codex;
    private readonly CodexUsageService _usage;
    private readonly PixelPetController _petController;
    private readonly ScreenCaptureService _screenCapture = new();
    private readonly DispatcherTimer _screenWatchTimer = new();
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
        _codex = new CodexAppServerService(_settings);
        _usage = new CodexUsageService(_settings);
        _petController = new PixelPetController(PetImage, _settings.PetPackPath);
        _screenWatchTimer.Tick += async (_, _) => await LookAtScreenAsync(automatic: true);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppLog.Write($"Window loaded. Version={Environment.Version}; Settings={AppSettings.SettingsPath}");
        Left = SystemParameters.WorkArea.Right - ActualWidth - 18;
        Top = SystemParameters.WorkArea.Bottom - ActualHeight - 18;
        Topmost = _settings.StayOnTop;
        UpdateContextHint();
        UpdateHotKeyPresentation();

        CreateTrayIcon();
        SetPetState(PetState.Idle);
        StartIdleAnimation();
        ConfigureScreenWatching();

        try { RegisterConfiguredHotKey(); }
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
                ShowMessage($"没有读到选中的文字。请先选中一段文字，再按 {CurrentHotKey.DisplayName}。",
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
            _ => ($"选中文字后按 {CurrentHotKey.DisplayName}", System.Windows.Media.Color.FromRgb(104, 233, 255))
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
        menu.Items.Add("看一眼屏幕并吐槽", null,
            async (_, _) => await LookAtScreenAsync(automatic: false));
        menu.Items.Add("在 Codex 中打开宠物对话", null, (_, _) => OpenConversation());
        menu.Items.Add("新建宠物对话", null, (_, _) => ClearContext());
        menu.Items.Add("宠物库…", null, (_, _) => OpenPetLibrary());
        menu.Items.Add("设置…", null, (_, _) => OpenSettings());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            Text = $"Codex 桌宠 — {CurrentHotKey.DisplayName}",
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

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is null)
            return;
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        MoreButton.ContextMenu.IsOpen = true;
    }

    private void HidePet_Click(object sender, RoutedEventArgs e) => Hide();

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
        AppLog.Write($"Starting Codex request. Mode={(isSelection ? "selection" : "direct")}; " +
                     $"Conversation={CurrentConversationKey}");
        var answer = await _codex.AskAsync(input, isSelection, CurrentConversationKey,
            _requestCancellation.Token);
        AppLog.Write($"Codex request completed. AnswerLength={answer.Length}");

        UpdateContextHint();
        ShowMessage(answer, PetState.Ready);
    }

    private void UpdateContextHint() =>
        HintText.Text = _codex.GetConversationThreadId(CurrentConversationKey) is null
            ? "新宠物对话"
            : "持久宠物对话";

    private void ClearContext_Click(object sender, RoutedEventArgs e) => ClearContext();

    private void ClearContext()
    {
        _codex.ResetConversation(CurrentConversationKey);
        UpdateContextHint();
        ShowMessage("已经切换到新的宠物对话；旧对话仍保留在 Codex 历史中。", PetState.Idle);
    }

    private void FocusQuestion_Click(object sender, RoutedEventArgs e)
    {
        AnswerPanel.Visibility = Visibility.Visible;
        Activate();
        DirectQuestionBox.Focus();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenPetLibrary_Click(object sender, RoutedEventArgs e) => OpenPetLibrary();

    private void OpenPetLibrary()
    {
        var previousPath = _settings.PetPackPath;
        var dialog = new PetLibraryWindow(_settings) { Owner = this, Topmost = true };
        dialog.ShowDialog();
        if (!string.Equals(previousPath, _settings.PetPackPath, StringComparison.OrdinalIgnoreCase))
        {
            _petController.Reload(_settings.PetPackPath);
            ShowMessage("桌宠已经切换。", PetState.Ready);
        }
    }

    private void OpenSettings()
    {
        var wasTopmost = Topmost;
        var previousHotKey = _settings.HotKey;
        Topmost = false;
        var dialog = new SettingsWindow(_settings) { Owner = this, Topmost = true };
        if (dialog.ShowDialog() == true)
        {
            Topmost = _settings.StayOnTop;
            try
            {
                RegisterConfiguredHotKey();
            }
            catch (Exception ex)
            {
                _settings.HotKey = previousHotKey;
                _settings.Save();
                try { RegisterConfiguredHotKey(); } catch { }
                UpdateHotKeyPresentation();
                AppLog.Write($"New hotkey rejected and previous hotkey restored: {ex}");
                ShowMessage($"{ex.Message} 已恢复为 {CurrentHotKey.DisplayName}。", PetState.Error);
                return;
            }
            UpdateHotKeyPresentation();
            _petController.Reload(_settings.PetPackPath);
            ConfigureScreenWatching();
            UpdateContextHint();
            ShowMessage("设置已保存并应用。", PetState.Ready);
            AppLog.Write("Settings saved and applied.");
        }
        else
        {
            Topmost = wasTopmost;
        }
    }

    private HotKeyGesture CurrentHotKey =>
        HotKeyGesture.ParseOrDefault(_settings.HotKey);

    private string CurrentConversationKey
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.PetPackPath))
                return "builtin:pixel-fish";
            try { return "pet:" + Path.GetFullPath(_settings.PetPackPath).ToLowerInvariant(); }
            catch { return "pet:" + _settings.PetPackPath.Trim().ToLowerInvariant(); }
        }
    }

    private void ConfigureScreenWatching()
    {
        _screenWatchTimer.Stop();
        _screenWatchTimer.Interval = TimeSpan.FromMinutes(
            Math.Clamp(_settings.ScreenWatchingIntervalMinutes, 1, 240));
        if (_settings.ScreenWatchingEnabled)
            _screenWatchTimer.Start();
        AppLog.Write($"Screen watching configured. Enabled={_settings.ScreenWatchingEnabled}; " +
                     $"IntervalMinutes={_screenWatchTimer.Interval.TotalMinutes}");
    }

    private async void LookAtScreen_Click(object sender, RoutedEventArgs e) =>
        await LookAtScreenAsync(automatic: false);

    private async Task LookAtScreenAsync(bool automatic)
    {
        if (_busy || automatic && !_settings.ScreenWatchingEnabled)
            return;

        _busy = true;
        string? capturePath = null;
        var wasVisible = IsVisible;
        try
        {
            SetPetState(PetState.Capturing);
            StatusText.Text = "正在看一眼屏幕…";
            if (wasVisible)
            {
                Hide();
                await Task.Delay(120);
            }
            capturePath = _screenCapture.CaptureDesktop();
            if (wasVisible)
                Show();
            AnswerPanel.Visibility = Visibility.Visible;
            AnswerText.Text = "正在想一句桌面吐槽…";
            SetPetState(PetState.Thinking);
            _requestCancellation = new CancellationTokenSource();
            var comment = await _codex.CommentOnScreenAsync(capturePath,
                CurrentConversationKey, _requestCancellation.Token);
            ShowMessage(comment, PetState.Ready);
            AppLog.Write($"Screen comment completed. Automatic={automatic}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Screen comment failed: {ex}");
            if (!automatic)
                ShowMessage(FriendlyError(ex), PetState.Error);
            else
                SetPetState(PetState.Idle);
        }
        finally
        {
            if (wasVisible && !IsVisible)
                Show();
            ScreenCaptureService.DeleteCapture(capturePath);
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            _busy = false;
        }
    }

    private void OpenConversation_Click(object sender, RoutedEventArgs e) => OpenConversation();

    private void OpenConversation()
    {
        var threadId = _codex.GetConversationThreadId(CurrentConversationKey);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            ShowMessage("还没有宠物对话。先提一个问题，我会自动创建。", PetState.NeedsInput);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"codex://threads/{threadId}",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write($"Open pet conversation failed: {ex}");
            ShowMessage($"无法打开 Codex 对话。线程 ID：{threadId}", PetState.Error);
        }
    }

    private void RegisterConfiguredHotKey()
    {
        var gesture = CurrentHotKey;
        _hotKey?.Dispose();
        _hotKey = null;

        var hotKey = new GlobalHotKeyManager(this, gesture.Modifiers, gesture.Key,
            gesture.DisplayName);
        hotKey.Pressed += HotKey_Pressed;
        _hotKey = hotKey;
        AppLog.Write($"Global hotkey {gesture.DisplayName} registered successfully.");
    }

    private void UpdateHotKeyPresentation()
    {
        var displayName = CurrentHotKey.DisplayName;
        StatusText.Text = $"选中文字后按 {displayName}";
        if (AnswerText.Text.StartsWith("我已经就位。", StringComparison.Ordinal))
            AnswerText.Text = $"我已经就位。选中文字后按 {displayName}，我会给出简要回答。";
        if (_trayIcon is not null)
            _trayIcon.Text = $"Codex 桌宠 — {displayName}";
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
        _screenWatchTimer.Stop();
        _codex.Dispose();
        _petController.Dispose();
    }
}
