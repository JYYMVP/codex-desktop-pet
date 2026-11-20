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
    private const int SelectionHotKeyId = 0xC0DE;
    private const int OcrHotKeyId = 0xC0DF;
    private const int TogglePetHotKeyId = 0xC0E0;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly SelectionCaptureService _selectionCapture = new();
    private readonly OfficialPetsQuickChatService _officialPets = new();
    private readonly CodexAppServerService _codex;
    private readonly CodexUsageService _usage;
    private readonly PixelPetController _petController;
    private readonly CodexActivityMonitor _activityMonitor = new();
    private readonly CodexDesktopLifetimeMonitor _lifetimeMonitor = new();
    private readonly ScreenCaptureService _screenCapture = new();
    private readonly DispatcherTimer _screenWatchTimer = new();
    private readonly DispatcherTimer _windowWatchTimer = new();
    private readonly DispatcherTimer _commentBubbleTimer = new();
    private readonly DispatcherTimer _interactionAnimationTimer = new();
    private readonly DispatcherTimer _pointerLookTimer = new();
    private readonly DispatcherTimer _statusChipTimer = new();
    private readonly DispatcherTimer _idleActionTimer = new();
    private GlobalHotKeyManager? _hotKey;
    private GlobalHotKeyManager? _ocrHotKey;
    private GlobalHotKeyManager? _togglePetHotKey;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _trayVisibilityItem;
    private Forms.ToolStripMenuItem? _trayActivityItem;
    private CancellationTokenSource? _requestCancellation;
    private bool _allowClose;
    private bool _busy;
    private string _answerMarkdown =
        "我已经就位。选中文字后按 Ctrl+Alt+Q，我会给出简要回答。";
    private System.Windows.Point? _petPressPosition;
    private System.Windows.Point? _dragStartPointerScreen;
    private System.Windows.Point _dragStartWindowPosition;
    private double _lastDragPointerX;
    private bool _petDragged;
    private string? _lastForegroundIdentity;
    private string? _pendingForegroundIdentity;
    private DateTime _pendingForegroundSinceUtc;
    private DateTime _lastWindowCommentUtc = DateTime.MinValue;
    private CodexActivityState _externalActivityState = CodexActivityState.Idle;
    private PetState _currentPetState = PetState.Idle;

    public MainWindow()
    {
        InitializeComponent();
        _codex = new CodexAppServerService(_settings);
        _usage = new CodexUsageService(_settings);
        _petController = new PixelPetController(PetImage, _settings.PetPackPath);
        _screenWatchTimer.Tick += async (_, _) =>
            await LookAtScreenAsync(ScreenCommentTrigger.Periodic);
        _windowWatchTimer.Interval = TimeSpan.FromMilliseconds(750);
        _windowWatchTimer.Tick += async (_, _) => await CheckForegroundWindowAsync();
        _commentBubbleTimer.Interval = TimeSpan.FromSeconds(
            Math.Clamp(_settings.CommentBubbleSeconds, 4, 60));
        _commentBubbleTimer.Tick += (_, _) => HideCommentBubble(resetPetState: true);
        _interactionAnimationTimer.Interval = TimeSpan.FromMilliseconds(900);
        _interactionAnimationTimer.Tick += (_, _) =>
        {
            _interactionAnimationTimer.Stop();
            StartIdleAnimation();
            RestoreRestingPetState();
        };
        _pointerLookTimer.Interval = TimeSpan.FromMilliseconds(100);
        _pointerLookTimer.Tick += (_, _) => UpdatePointerLook();
        _statusChipTimer.Interval = TimeSpan.FromSeconds(2.2);
        _statusChipTimer.Tick += (_, _) =>
        {
            _statusChipTimer.Stop();
            if (_currentPetState == PetState.Ready)
                StatusChip.Visibility = Visibility.Collapsed;
        };
        _idleActionTimer.Tick += (_, _) => RunIdleAction();
        _activityMonitor.StatusChanged += ActivityMonitor_StatusChanged;
        _activityMonitor.SnapshotsChanged += ActivityMonitor_SnapshotsChanged;
        _lifetimeMonitor.CodexExited += (_, _) =>
        {
            _allowClose = true;
            Close();
        };
        SetAnswerMarkdown(_answerMarkdown);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var appVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
        AppLog.Write($"Window loaded. Version={appVersion}; Settings={AppSettings.SettingsPath}");
        if (!_settings.PetEnabled)
        {
            AppLog.Write("Desktop pet is disabled in plugin settings; exiting without registering hotkeys.");
            _allowClose = true;
            Close();
            return;
        }
        Left = SystemParameters.WorkArea.Right - ActualWidth - 18;
        Top = SystemParameters.WorkArea.Bottom - ActualHeight - 18;
        Topmost = _settings.StayOnTop;
        UpdateContextHint();
        UpdateHotKeyPresentation();

        CreateTrayIcon();
        SetPetState(PetState.Idle);
        StartIdleAnimation();
        ConfigureScreenWatching();
        ConfigureWindowChangeComments();
        ConfigureCodexActivityMonitoring();
        ConfigureCodexDesktopLifetime();
        ConfigurePointerLook();
        ConfigureIdleActivity();

        try { RegisterConfiguredHotKey(); }
        catch (Exception ex)
        {
            AppLog.Write($"Hotkey registration failed: {ex}");
            ShowMessage(ex.Message, PetState.Error);
        }

        if (_settings.OfficialPetsCompanionMode || _settings.PetHidden)
            Hide();
    }

    private async void HotKey_Pressed(object? sender, EventArgs e)
    {
        AppLog.Write($"Hotkey received. Busy={_busy}");
        if (_busy)
            return;

        _busy = true;
        if (!_settings.OfficialPetsCompanionMode && !IsVisible)
            ShowStandalonePet(persistVisibility: true);
        if (!_settings.OfficialPetsCompanionMode)
        {
            AnswerPanel.Visibility = Visibility.Collapsed;
            SetPetState(PetState.Capturing);
        }

        try
        {
            var selectedText = await _selectionCapture.CaptureAsync();
            AppLog.Write($"Selection captured. Length={selectedText?.Length ?? 0}");
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                NotifyOrShow($"没有读到选中的文字。请先选中一段文字，再按 {CurrentHotKey.DisplayName}。",
                    PetState.NeedsInput, Forms.ToolTipIcon.Warning);
                return;
            }

            if (_settings.OfficialPetsCompanionMode)
            {
                await _officialPets.PrefillSelectionQuestionAsync(
                    selectedText,
                    _settings.Language,
                    _settings.MaximumSelectionCharacters);
                AppLog.Write("Selection inserted into official Pets Quick Chat; awaiting user confirmation.");
                ShowTrayNotification("已填入官方 Pets Quick Chat，检查后按 Enter 发送。",
                    Forms.ToolTipIcon.Info);
                return;
            }

            await AskAndRecordAsync(selectedText, isSelection: true);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Request failed: {ex}");
            NotifyOrShow(FriendlyError(ex), PetState.Error, Forms.ToolTipIcon.Error);
        }
        finally
        {
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            _busy = false;
            ApplyExternalActivityState();
        }
    }

    private async void OcrHotKey_Pressed(object? sender, EventArgs e) =>
        await RecognizeOcrRegionAsync();

    private void NotifyOrShow(string message, PetState state, Forms.ToolTipIcon icon)
    {
        if (_settings.OfficialPetsCompanionMode)
            ShowTrayNotification(message, icon);
        else
            ShowMessage(message, state);
    }

    private void ShowTrayNotification(string message, Forms.ToolTipIcon icon)
    {
        _trayIcon?.ShowBalloonTip(3500, "Codex Pets 增强", message, icon);
    }

    private void ShowMessage(string message, PetState state)
    {
        HideCommentBubble(resetPetState: false);
        SetComposerOpen(false);
        SetAnswerMarkdown(message);
        AnswerPanel.Visibility = Visibility.Visible;
        SetPetState(state);
    }

    private void ShowCommentBubble(string comment)
    {
        _commentBubbleTimer.Stop();
        _commentBubbleTimer.Interval = TimeSpan.FromSeconds(
            Math.Clamp(_settings.CommentBubbleSeconds, 4, 60));
        AnswerPanel.Visibility = Visibility.Collapsed;
        SetComposerOpen(false);
        CommentText.Text = comment.Trim();
        CommentBubble.Visibility = Visibility.Visible;
        SetPetState(PetState.Ready);
        _commentBubbleTimer.Start();
    }

    private void HideCommentBubble(bool resetPetState)
    {
        _commentBubbleTimer.Stop();
        CommentBubble.Visibility = Visibility.Collapsed;
        if (resetPetState && !_busy)
            RestoreRestingPetState();
    }

    private void SetAnswerMarkdown(string markdown)
    {
        _answerMarkdown = markdown ?? string.Empty;
        MarkdownRenderer.Render(AnswerText, _answerMarkdown);
    }

    private void SetPetState(PetState state)
    {
        _currentPetState = state;
        StopThinkingAnimation();
        var (status, color) = state switch
        {
            PetState.Capturing => ("正在读取", System.Windows.Media.Color.FromRgb(139, 227, 255)),
            PetState.Thinking => ("工作中", System.Windows.Media.Color.FromRgb(187, 139, 255)),
            PetState.Ready => ("已就绪", System.Windows.Media.Color.FromRgb(104, 233, 255)),
            PetState.NeedsInput => ("需要输入", System.Windows.Media.Color.FromRgb(255, 210, 112)),
            PetState.Error => ("已阻塞", System.Windows.Media.Color.FromRgb(255, 122, 146)),
            _ => (string.Empty, System.Windows.Media.Color.FromRgb(104, 233, 255))
        };

        StatusText.Text = status;
        _statusChipTimer.Stop();
        StatusChip.Visibility = string.IsNullOrWhiteSpace(status)
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (state == PetState.Ready)
            _statusChipTimer.Start();
        GlowRing.Stroke = new SolidColorBrush(color) { Opacity = 0.65 };
        _petController.SetState(state);
        if (state == PetState.Thinking)
            StartThinkingAnimation();
    }

    private void StartIdleAnimation()
    {
        if (_settings.ReduceMotion)
        {
            PetTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            PetTranslate.Y = 0;
            return;
        }
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
        if (_settings.ReduceMotion)
            return;
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
        if (_settings.OfficialPetsCompanionMode)
            menu.Items.Add("唤起官方 Pets", null, async (_, _) => await ShowFromTrayAsync());
        else
        {
            _trayVisibilityItem = new Forms.ToolStripMenuItem(
                $"显示 / 隐藏桌宠（{CurrentTogglePetHotKey.DisplayName}）",
                null, (_, _) => TogglePetVisibility());
            menu.Items.Add(_trayVisibilityItem);
            _trayActivityItem = new Forms.ToolStripMenuItem("Codex 活动");
            menu.Items.Add(_trayActivityItem);
            RefreshTrayActivityMenu();
        }
        if (_settings.OfficialPetsCompanionMode)
            menu.Items.Add("官方 Pets 诊断…", null, (_, _) => ShowOfficialPetsDiagnostics());
        if (!_settings.OfficialPetsCompanionMode)
        {
            menu.Items.Add("语音输入（Windows）", null,
                async (_, _) => await StartVoiceTypingAsync());
            menu.Items.Add($"OCR 框选识别（{CurrentOcrHotKey.DisplayName}）", null,
                async (_, _) => await RecognizeOcrRegionAsync());
            menu.Items.Add("查看 Codex 套餐额度", null,
                async (_, _) => await ShowCodexUsageAsync());
            menu.Items.Add("看一眼屏幕并吐槽", null,
                async (_, _) => await LookAtScreenAsync(ScreenCommentTrigger.Manual));
            menu.Items.Add("在 Codex 中打开宠物对话", null, (_, _) => OpenConversation());
            menu.Items.Add("新建宠物对话", null, (_, _) => ClearContext());
            menu.Items.Add("宠物库…", null, (_, _) => OpenPetLibrary());
        }
        menu.Items.Add("设置…", null, (_, _) => OpenSettings());
        menu.Items.Add("彻底停用（从插件页重新启用）", null, (_, _) => DisableAndExitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            Text = $"Codex 桌宠 — {CurrentHotKey.DisplayName}",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += async (_, _) =>
        {
            if (_settings.OfficialPetsCompanionMode)
                await ShowFromTrayAsync();
            else
                TogglePetVisibility();
        };
    }

    private async Task ShowFromTrayAsync()
    {
        if (_settings.OfficialPetsCompanionMode)
        {
            await _officialPets.ShowControlsAsync();
            return;
        }

        ShowStandalonePet(persistVisibility: true);
    }

    internal async Task ShowFromExternalRequestAsync()
    {
        AppLog.Write("External show request received.");
        if (_settings.OfficialPetsCompanionMode)
        {
            await _officialPets.ShowControlsAsync();
            return;
        }

        ShowStandalonePet(persistVisibility: true);
        Activate();
    }

    private void ShowStandalonePet(bool persistVisibility)
    {
        if (persistVisibility && _settings.PetHidden)
        {
            _settings.PetHidden = false;
            _settings.Save();
        }
        Show();
        WindowState = WindowState.Normal;
        Topmost = false;
        Topmost = _settings.StayOnTop;
        AppLog.Write($"Standalone pet shown. Visible={IsVisible}; Persisted={!_settings.PetHidden}.");
    }

    private void HideStandalonePet(bool persistVisibility)
    {
        if (persistVisibility && !_settings.PetHidden)
        {
            _settings.PetHidden = true;
            _settings.Save();
        }
        SetComposerOpen(false);
        Hide();
    }

    private void TogglePetVisibility()
    {
        if (_settings.OfficialPetsCompanionMode)
        {
            ShowTrayNotification("官方 Pets 增强模式正在使用独立的 Quick Chat。", Forms.ToolTipIcon.Info);
            return;
        }
        if (IsVisible)
            HideStandalonePet(persistVisibility: true);
        else
            ShowStandalonePet(persistVisibility: true);
        AppLog.Write($"Pet visibility toggled. Visible={IsVisible}; HotKey={CurrentTogglePetHotKey.DisplayName}");
    }

    private void TogglePetHotKey_Pressed(object? sender, EventArgs e) => TogglePetVisibility();

    private void ShowOfficialPetsDiagnostics()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowMessage(_officialPets.BuildDiagnosticMarkdown(), PetState.Idle);
        AppLog.Write("Official Pets diagnostic report displayed.");
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void DisableAndExitApplication()
    {
        _settings.PetEnabled = false;
        _settings.Save();
        AppLog.Write("Desktop pet disabled by the user. Plugin Show Pet or Enable Pet can restore it.");
        ExitApplication();
    }

    private static string FriendlyError(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("not recognized", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("找不到 Codex CLI", StringComparison.OrdinalIgnoreCase))
            return "没有找到 Codex CLI。请打开设置文件并填写 CodexExecutable 的完整路径。";
        return message;
    }

    private async void OcrRegion_Click(object sender, RoutedEventArgs e) =>
        await RecognizeOcrRegionAsync();

    private async Task RecognizeOcrRegionAsync()
    {
        if (_busy)
            return;

        _busy = true;
        string? snapshotPath = null;
        string? regionPath = null;
        var wasVisible = IsVisible;
        try
        {
            if (wasVisible)
            {
                SetComposerOpen(false);
                Hide();
                await Task.Delay(140);
            }

            snapshotPath = _screenCapture.CaptureDesktopSnapshot();
            var selector = new OcrRegionSelectionWindow(snapshotPath);
            if (selector.ShowDialog() != true)
            {
                AppLog.Write("OCR region selection cancelled.");
                return;
            }

            regionPath = _screenCapture.CropSnapshot(snapshotPath,
                selector.NormalizedSelection);
            ScreenCaptureService.DeleteCapture(snapshotPath);
            snapshotPath = null;
            AppLog.Write("OCR region captured; starting Codex image request.");

            ShowStandalonePet(persistVisibility: true);
            WindowState = WindowState.Normal;
            AnswerPanel.Visibility = Visibility.Visible;
            SetAnswerMarkdown("正在识别框选区域…");
            SetPetState(PetState.Thinking);
            _requestCancellation = new CancellationTokenSource();
            var answer = await _codex.AskAboutOcrRegionAsync(regionPath,
                CurrentConversationKey, _requestCancellation.Token);
            ShowMessage(answer, PetState.Ready);
            AppLog.Write($"OCR region request completed. AnswerLength={answer.Length}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"OCR region request failed: {ex}");
            if (_settings.OfficialPetsCompanionMode && !IsVisible)
                ShowTrayNotification(FriendlyError(ex), Forms.ToolTipIcon.Error);
            else
            {
                if (!IsVisible)
                    ShowStandalonePet(persistVisibility: true);
                ShowMessage(FriendlyError(ex), PetState.Error);
            }
        }
        finally
        {
            ScreenCaptureService.DeleteCapture(snapshotPath);
            ScreenCaptureService.DeleteCapture(regionPath);
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            _busy = false;
            ApplyExternalActivityState();
            if (wasVisible && !IsVisible && !_settings.OfficialPetsCompanionMode)
                Show();
        }
    }

    private void CopyAnswer_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_answerMarkdown))
            System.Windows.Clipboard.SetText(_answerMarkdown);
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
        SetAnswerMarkdown("正在查询 Codex 套餐额度…");
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
            ApplyExternalActivityState();
        }
    }

    private void ToggleAnswer_Click(object sender, RoutedEventArgs e) =>
        AnswerPanel.Visibility = AnswerPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        PetContextMenu.PlacementTarget = MoreButton;
        PetContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        PetContextMenu.IsOpen = true;
    }

    private void HidePet_Click(object sender, RoutedEventArgs e) =>
        HideStandalonePet(persistVisibility: true);

    private void CommentBubble_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HideCommentBubble(resetPetState: true);
        e.Handled = true;
    }

    private void CommentBubble_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        _commentBubbleTimer.Stop();

    private void CommentBubble_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (CommentBubble.Visibility == Visibility.Visible)
            _commentBubbleTimer.Start();
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e) => DisableAndExitApplication();

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
        SetComposerOpen(false);
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
            ApplyExternalActivityState();
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
        HideCommentBubble(resetPetState: false);
        SetComposerOpen(!ComposerPopup.IsOpen);
        if (!ComposerPopup.IsOpen)
            return;
        Activate();
        DirectQuestionBox.Focus();
    }

    private async void VoiceTyping_Click(object sender, RoutedEventArgs e) =>
        await StartVoiceTypingAsync();

    private async Task StartVoiceTypingAsync()
    {
        try
        {
            if (_settings.OfficialPetsCompanionMode)
            {
                await _officialPets.ShowControlsAsync();
            }
            else
            {
                ShowStandalonePet(persistVisibility: true);
                HideCommentBubble(resetPetState: false);
                SetComposerOpen(true);
                Activate();
                DirectQuestionBox.Focus();
            }

            await Task.Delay(180);
            VoiceTypingService.Start();
            AppLog.Write("Windows voice typing opened for the pet question field.");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Voice typing failed: {ex}");
            NotifyOrShow("无法打开 Windows 语音输入。请确认系统已启用语音服务，或直接按 Win+H。",
                PetState.Error, Forms.ToolTipIcon.Warning);
        }
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
            ConfigurePointerLook();
            ShowMessage("桌宠已经切换。", PetState.Ready);
        }
    }

    private void OpenSettings()
    {
        var wasTopmost = Topmost;
        var previousHotKey = _settings.HotKey;
        var previousOcrHotKey = _settings.OcrHotKey;
        var previousTogglePetHotKey = _settings.TogglePetHotKey;
        var previousOfficialPetsMode = _settings.OfficialPetsCompanionMode;
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
                _settings.OcrHotKey = previousOcrHotKey;
                _settings.TogglePetHotKey = previousTogglePetHotKey;
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
            ConfigureWindowChangeComments();
            ConfigureCodexActivityMonitoring();
            ConfigureCodexDesktopLifetime();
            ConfigurePointerLook();
            ConfigureIdleActivity();
            UpdateContextHint();
            RecreateTrayIcon();
            if (_settings.OfficialPetsCompanionMode || _settings.PetHidden)
            {
                Hide();
                if (_settings.OfficialPetsCompanionMode)
                    ShowTrayNotification("官方 Pets 增强模式已启用。选中文字后使用全局快捷键。",
                        Forms.ToolTipIcon.Info);
            }
            else
            {
                if (previousOfficialPetsMode || !IsVisible)
                    ShowStandalonePet(persistVisibility: false);
                ShowMessage("设置已保存并应用。", PetState.Ready);
            }
            AppLog.Write("Settings saved and applied.");
        }
        else
        {
            Topmost = wasTopmost;
        }
    }

    private void RecreateTrayIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayActivityItem = null;
        CreateTrayIcon();
    }

    private HotKeyGesture CurrentHotKey =>
        HotKeyGesture.ParseOrDefault(_settings.HotKey);

    private HotKeyGesture CurrentOcrHotKey =>
        HotKeyGesture.ParseOrDefault(_settings.OcrHotKey);

    private HotKeyGesture CurrentTogglePetHotKey =>
        HotKeyGesture.ParseOrDefault(_settings.TogglePetHotKey);

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
        if (_settings.ScreenWatchingEnabled && !_settings.OfficialPetsCompanionMode)
            _screenWatchTimer.Start();
        AppLog.Write($"Screen watching configured. Enabled={_screenWatchTimer.IsEnabled}; " +
                     $"IntervalMinutes={_screenWatchTimer.Interval.TotalMinutes}");
    }

    private async void LookAtScreen_Click(object sender, RoutedEventArgs e) =>
        await LookAtScreenAsync(ScreenCommentTrigger.Manual);

    private async Task LookAtScreenAsync(ScreenCommentTrigger trigger)
    {
        var automatic = trigger != ScreenCommentTrigger.Manual;
        if (_settings.OfficialPetsCompanionMode || _busy ||
            automatic && (!IsVisible || _settings.PetHidden) ||
            automatic && CommentBubble.Visibility == Visibility.Visible ||
            automatic && (AnswerPanel.Visibility == Visibility.Visible ||
                          ComposerPopup.IsOpen) ||
            trigger == ScreenCommentTrigger.Periodic && !_settings.ScreenWatchingEnabled ||
            trigger == ScreenCommentTrigger.WindowChange && !_settings.WindowChangeCommentsEnabled)
        {
            if (automatic)
                AppLog.Write($"Automatic screen comment skipped. Trigger={trigger}; " +
                             $"Visible={IsVisible}; Hidden={_settings.PetHidden}; " +
                             $"BubbleVisible={CommentBubble.Visibility == Visibility.Visible}; " +
                             $"AnswerVisible={AnswerPanel.Visibility == Visibility.Visible}; " +
                             $"ComposerVisible={ComposerPopup.IsOpen}; Busy={_busy}");
            return;
        }

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
            HideCommentBubble(resetPetState: false);
            AnswerPanel.Visibility = Visibility.Collapsed;
            SetPetState(PetState.Thinking);
            StatusText.Text = "工作中…";
            _requestCancellation = new CancellationTokenSource();
            var comment = await _codex.CommentOnScreenAsync(capturePath,
                CurrentConversationKey,
                triggeredByWindowChange: trigger == ScreenCommentTrigger.WindowChange,
                cancellationToken: _requestCancellation.Token);
            ShowCommentBubble(comment);
            AppLog.Write($"Screen comment completed. Trigger={trigger}");
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
            if (wasVisible && !IsVisible && !_settings.OfficialPetsCompanionMode)
                Show();
            ScreenCaptureService.DeleteCapture(capturePath);
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            _busy = false;
            ApplyExternalActivityState();
        }
    }

    private void ConfigureCodexActivityMonitoring()
    {
        if (_settings.FollowCodexActivity && !_settings.OfficialPetsCompanionMode)
            _activityMonitor.Start();
        else
            _activityMonitor.Stop();
        AppLog.Write($"Codex activity monitoring configured. " +
                     $"Enabled={_settings.FollowCodexActivity && !_settings.OfficialPetsCompanionMode}");
    }

    private void ConfigureCodexDesktopLifetime()
    {
        _lifetimeMonitor.Stop();
        if (_settings.BindLifetimeToCodex)
            _lifetimeMonitor.Start();
        AppLog.Write($"Codex-bound lifetime configured. Enabled={_settings.BindLifetimeToCodex}");
    }

    private void ConfigureIdleActivity()
    {
        _idleActionTimer.Stop();
        StartIdleAnimation();
        if (_settings.ReduceMotion || _settings.IdleActivityLevel <= 0 ||
            _settings.OfficialPetsCompanionMode)
            return;
        ScheduleNextIdleAction();
        _idleActionTimer.Start();
        AppLog.Write($"Idle activity configured. Level={_settings.IdleActivityLevel}; " +
                     $"ReduceMotion={_settings.ReduceMotion}");
    }

    private void ScheduleNextIdleAction()
    {
        var (minimum, maximum) = _settings.IdleActivityLevel >= 2 ? (10, 22) : (28, 52);
        _idleActionTimer.Interval = TimeSpan.FromSeconds(Random.Shared.Next(minimum, maximum + 1));
    }

    private void RunIdleAction()
    {
        ScheduleNextIdleAction();
        if (_settings.ReduceMotion || _busy || !IsVisible || ComposerPopup.IsOpen ||
            AnswerPanel.Visibility == Visibility.Visible ||
            CommentBubble.Visibility == Visibility.Visible ||
            _currentPetState is not (PetState.Idle or PetState.Ready))
            return;
        var state = Random.Shared.Next(3) == 0 ? PetState.Jumping : PetState.Waving;
        PlayPetInteraction(state, TimeSpan.FromMilliseconds(900),
            hop: state == PetState.Jumping);
    }

    private void ConfigurePointerLook()
    {
        _pointerLookTimer.Stop();
        if (_settings.FollowPointerWithPet && _petController.SupportsLookDirections &&
            !_settings.OfficialPetsCompanionMode)
            _pointerLookTimer.Start();
        AppLog.Write($"Pointer look configured. Enabled={_pointerLookTimer.IsEnabled}; " +
                     $"Supported={_petController.SupportsLookDirections}");
    }

    private void UpdatePointerLook()
    {
        if (!_settings.FollowPointerWithPet || !_petController.SupportsLookDirections ||
            !IsVisible || _busy || _interactionAnimationTimer.IsEnabled ||
            _currentPetState is not (PetState.Idle or PetState.Ready or
                PetState.LookFirstHalf or PetState.LookSecondHalf))
            return;
        try
        {
            var petCenter = PetImage.PointToScreen(new System.Windows.Point(
                PetImage.ActualWidth / 2, PetImage.ActualHeight / 2));
            var cursor = Forms.Cursor.Position;
            var dx = cursor.X - petCenter.X;
            var dy = cursor.Y - petCenter.Y;
            if (dx * dx + dy * dy < 20 * 20)
                return;
            var degrees = Math.Atan2(dx, -dy) * 180 / Math.PI;
            if (degrees < 0)
                degrees += 360;
            var index = (int)Math.Round(degrees / 22.5) % 16;
            if (_petController.SetLookDirection(index))
                _currentPetState = index < 8 ? PetState.LookFirstHalf : PetState.LookSecondHalf;
        }
        catch (InvalidOperationException)
        {
            // Window coordinates can be unavailable briefly while showing or hiding.
        }
    }

    private void ActivityMonitor_StatusChanged(object? sender, CodexActivityState state)
    {
        _externalActivityState = state;
        if (!_busy)
            ApplyExternalActivityState();
    }

    private void ActivityMonitor_SnapshotsChanged(
        object? sender,
        IReadOnlyList<CodexActivitySnapshot> snapshots)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() =>
            {
                RefreshTrayActivityMenu();
                if (!_busy)
                    ApplyExternalActivityState();
            });
            return;
        }
        RefreshTrayActivityMenu();
        if (!_busy)
            ApplyExternalActivityState();
    }

    private void RefreshTrayActivityMenu()
    {
        if (_trayActivityItem is null)
            return;
        var snapshots = _activityMonitor.CurrentSnapshots;
        _trayActivityItem.DropDownItems.Clear();
        _trayActivityItem.Text = snapshots.Count == 0
            ? "Codex 活动：空闲"
            : $"Codex 活动（{snapshots.Count}）";
        if (snapshots.Count == 0)
        {
            _trayActivityItem.DropDownItems.Add(new Forms.ToolStripMenuItem("当前没有活动聊天")
            {
                Enabled = false
            });
            return;
        }

        foreach (var snapshot in snapshots.Take(12))
        {
            var state = DescribeActivity(snapshot.State, snapshot.Event);
            var shortHash = snapshot.SessionHash[..Math.Min(6, snapshot.SessionHash.Length)]
                .ToUpperInvariant();
            _trayActivityItem.DropDownItems.Add(new Forms.ToolStripMenuItem(
                $"{shortHash} · {state} · {snapshot.UpdatedAtUtc.ToLocalTime():HH:mm:ss}")
            {
                Enabled = false,
                ToolTipText = string.IsNullOrWhiteSpace(snapshot.Event)
                    ? "Codex 生命周期状态"
                    : snapshot.Event
            });
        }
    }

    private void ApplyExternalActivityState()
    {
        if (_busy || !_settings.FollowCodexActivity || _settings.OfficialPetsCompanionMode)
            return;
        SetPetState(_externalActivityState switch
        {
            CodexActivityState.Running => PetState.Thinking,
            CodexActivityState.NeedsInput => PetState.NeedsInput,
            CodexActivityState.Ready => PetState.Ready,
            CodexActivityState.Blocked => PetState.Error,
            _ => PetState.Idle
        });
        if (_externalActivityState == CodexActivityState.Idle)
            return;

        var snapshots = _activityMonitor.CurrentSnapshots;
        var representative = snapshots.FirstOrDefault(snapshot =>
            snapshot.State == _externalActivityState);
        var label = DescribeActivity(_externalActivityState, representative?.Event);
        var activeCount = snapshots.Count(snapshot =>
            snapshot.State is CodexActivityState.Running or CodexActivityState.NeedsInput);
        StatusText.Text = activeCount > 1 ? $"{label} · {activeCount} 个对话" : label;
        StatusChip.Visibility = Visibility.Visible;
    }

    internal static string DescribeActivity(CodexActivityState state, string? eventName) =>
        state switch
        {
            CodexActivityState.NeedsInput => "等待你确认",
            CodexActivityState.Blocked => "任务已阻塞",
            CodexActivityState.Ready => "任务已完成",
            CodexActivityState.Running => eventName switch
            {
                "UserPromptSubmit" => "正在接收任务",
                "PreModelRequest" => "正在思考",
                "PreToolUse" => "正在执行工具",
                "PostToolUse" => "正在整理结果",
                "SubagentStart" => "子任务工作中",
                "SubagentStop" => "正在汇总子任务",
                _ => "Codex 工作中"
            },
            _ => "空闲"
        };

    private void RestoreRestingPetState()
    {
        if (_settings.FollowCodexActivity && !_settings.OfficialPetsCompanionMode)
            ApplyExternalActivityState();
        else if (!_busy)
            SetPetState(PetState.Idle);
    }

    private void ConfigureWindowChangeComments()
    {
        _windowWatchTimer.Stop();
        var current = ForegroundWindowService.GetCurrent();
        _lastForegroundIdentity = current?.Identity;
        _pendingForegroundIdentity = null;
        if (_settings.WindowChangeCommentsEnabled && !_settings.OfficialPetsCompanionMode)
            _windowWatchTimer.Start();
        AppLog.Write($"Window change comments configured. " +
                     $"Enabled={_windowWatchTimer.IsEnabled}; " +
                     $"CooldownMinutes={_settings.WindowChangeCommentCooldownMinutes}");
    }

    private async Task CheckForegroundWindowAsync()
    {
        if (_settings.OfficialPetsCompanionMode ||
            !_settings.WindowChangeCommentsEnabled || _busy)
            return;

        var current = ForegroundWindowService.GetCurrent();
        if (current is null || current.ProcessId == Environment.ProcessId)
            return;
        if (string.Equals(current.Identity, _lastForegroundIdentity, StringComparison.Ordinal))
        {
            _pendingForegroundIdentity = null;
            return;
        }

        if (!string.Equals(current.Identity, _pendingForegroundIdentity, StringComparison.Ordinal))
        {
            _pendingForegroundIdentity = current.Identity;
            _pendingForegroundSinceUtc = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - _pendingForegroundSinceUtc < TimeSpan.FromMilliseconds(1500))
            return;

        _lastForegroundIdentity = current.Identity;
        _pendingForegroundIdentity = null;
        var cooldown = TimeSpan.FromMinutes(
            Math.Clamp(_settings.WindowChangeCommentCooldownMinutes, 1, 60));
        if (DateTime.UtcNow - _lastWindowCommentUtc < cooldown)
        {
            AppLog.Write($"Window change comment skipped by cooldown. " +
                         $"Process={current.ProcessName}");
            return;
        }

        _lastWindowCommentUtc = DateTime.UtcNow;
        AppLog.Write($"Stable foreground window change detected. Process={current.ProcessName}");
        await LookAtScreenAsync(ScreenCommentTrigger.WindowChange);
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
        var ocrGesture = CurrentOcrHotKey;
        var toggleGesture = CurrentTogglePetHotKey;
        _hotKey?.Dispose();
        _hotKey = null;
        _ocrHotKey?.Dispose();
        _ocrHotKey = null;
        _togglePetHotKey?.Dispose();
        _togglePetHotKey = null;

        var hotKey = new GlobalHotKeyManager(this, SelectionHotKeyId,
            gesture.Modifiers, gesture.Key, gesture.DisplayName);
        hotKey.Pressed += HotKey_Pressed;
        _hotKey = hotKey;

        try
        {
            var ocrHotKey = new GlobalHotKeyManager(this, OcrHotKeyId,
                ocrGesture.Modifiers, ocrGesture.Key, ocrGesture.DisplayName);
            ocrHotKey.Pressed += OcrHotKey_Pressed;
            _ocrHotKey = ocrHotKey;
            var toggleHotKey = new GlobalHotKeyManager(this, TogglePetHotKeyId,
                toggleGesture.Modifiers, toggleGesture.Key, toggleGesture.DisplayName);
            toggleHotKey.Pressed += TogglePetHotKey_Pressed;
            _togglePetHotKey = toggleHotKey;
            AppLog.Write($"Global hotkeys registered. Selection={gesture.DisplayName}; " +
                         $"OCR={ocrGesture.DisplayName}; TogglePet={toggleGesture.DisplayName}");
        }
        catch
        {
            _ocrHotKey?.Dispose();
            _ocrHotKey = null;
            _togglePetHotKey?.Dispose();
            _togglePetHotKey = null;
            _hotKey?.Dispose();
            _hotKey = null;
            throw;
        }
    }

    private void UpdateHotKeyPresentation()
    {
        var displayName = CurrentHotKey.DisplayName;
        var ocrDisplayName = CurrentOcrHotKey.DisplayName;
        var toggleDisplayName = CurrentTogglePetHotKey.DisplayName;
        PetBody.ToolTip = $"单击：快速提问\n拖动：移动桌宠\n显示/隐藏：{toggleDisplayName}";
        PetOcrMenuItem.Header = $"OCR 框选识别（{ocrDisplayName}）";
        PetHideMenuItem.Header = $"隐藏桌宠（{toggleDisplayName}）";
        if (_answerMarkdown.StartsWith("我已经就位。", StringComparison.Ordinal))
            SetAnswerMarkdown($"我已经就位。选中文字后按 {displayName}，我会给出简要回答。");
        if (_trayIcon is not null)
            _trayIcon.Text = $"Codex 桌宠 — {displayName}";
        if (_trayVisibilityItem is not null)
            _trayVisibilityItem.Text = $"显示 / 隐藏桌宠（{toggleDisplayName}）";
    }

    private void PetBody_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _petPressPosition = e.GetPosition(this);
        _dragStartPointerScreen = GetPointerScreenPosition();
        _dragStartWindowPosition = new System.Windows.Point(Left, Top);
        _lastDragPointerX = _dragStartPointerScreen?.X ?? 0;
        _petDragged = false;
        PetBody.CaptureMouse();
        e.Handled = true;
    }

    private void PetBody_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_petPressPosition is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(this);
        if (!_petDragged)
        {
            var movedFarEnough =
                Math.Abs(current.X - _petPressPosition.Value.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(current.Y - _petPressPosition.Value.Y) >= SystemParameters.MinimumVerticalDragDistance;
            if (!movedFarEnough)
                return;
            _petDragged = true;
            SetComposerOpen(false);
            PetTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        }

        if (_dragStartPointerScreen is not null)
        {
            var pointer = GetPointerScreenPosition();
            var horizontalDelta = pointer.X - _lastDragPointerX;
            if (Math.Abs(horizontalDelta) >= 1.5)
                SetPetState(horizontalDelta > 0 ? PetState.RunningRight : PetState.RunningLeft);
            _lastDragPointerX = pointer.X;
            MoveWindowKeepingControlsVisible(
                _dragStartWindowPosition.X + pointer.X - _dragStartPointerScreen.Value.X,
                _dragStartWindowPosition.Y + pointer.Y - _dragStartPointerScreen.Value.Y);
        }
        e.Handled = true;
    }

    private void PetBody_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        PetBody.ReleaseMouseCapture();
        if (_petPressPosition is not null && !_petDragged)
        {
            if (!_busy)
            {
                _interactionAnimationTimer.Stop();
                SetPetState(PetState.Waving);
                _interactionAnimationTimer.Start();
            }
            if (CommentBubble.Visibility == Visibility.Visible)
                HideCommentBubble(resetPetState: true);
            else if (AnswerPanel.Visibility == Visibility.Visible)
                AnswerPanel.Visibility = Visibility.Collapsed;
            else
            {
                SetComposerOpen(!ComposerPopup.IsOpen);
                if (ComposerPopup.IsOpen)
                {
                    Activate();
                    DirectQuestionBox.Focus();
                }
            }
            AppLog.Write($"Pet clicked. AnswerVisible={AnswerPanel.Visibility == Visibility.Visible}; " +
                         $"ComposerVisible={ComposerPopup.IsOpen}");
        }
        else if (_petDragged)
        {
            StartIdleAnimation();
            RestoreRestingPetState();
            AppLog.Write("Pet drag completed and resting animation restored.");
        }

        _petPressPosition = null;
        _dragStartPointerScreen = null;
        _petDragged = false;
        e.Handled = true;
    }

    private void PetBody_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_settings.ReduceMotion || _busy || _petDragged ||
            PetBody.IsMouseCaptured || ComposerPopup.IsOpen)
            return;
        PlayPetInteraction(PetState.Jumping, TimeSpan.FromMilliseconds(720), hop: true);
    }

    private void PlayPetAction_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not System.Windows.Controls.MenuItem { Tag: string value } ||
            !Enum.TryParse<PetState>(value, out var state))
            return;
        PlayPetInteraction(state, TimeSpan.FromMilliseconds(1200),
            hop: state == PetState.Jumping);
    }

    private void PlayPetInteraction(PetState state, TimeSpan duration, bool hop)
    {
        _interactionAnimationTimer.Stop();
        _interactionAnimationTimer.Interval = duration;
        SetPetState(state);
        if (hop && !_settings.ReduceMotion)
        {
            PetTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(0, -18, TimeSpan.FromMilliseconds(240))
                {
                    AutoReverse = true,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }
        _interactionAnimationTimer.Start();
    }

    private void SetComposerOpen(bool open)
    {
        ComposerPopup.IsOpen = open;
        ActionDock.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
    }

    private System.Windows.Point GetPointerScreenPosition()
    {
        var cursor = Forms.Cursor.Position;
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformFromDevice.Transform(
                   new System.Windows.Point(cursor.X, cursor.Y))
               ?? new System.Windows.Point(cursor.X, cursor.Y);
    }

    private void MoveWindowKeepingControlsVisible(double requestedLeft, double requestedTop)
    {
        var petTopLeft = PetBody.TranslatePoint(new System.Windows.Point(0, 0), this);
        var dockTopLeft = ActionDock.TranslatePoint(new System.Windows.Point(0, 0), this);
        var contentLeft = Math.Min(petTopLeft.X, dockTopLeft.X);
        var contentTop = Math.Min(petTopLeft.Y, dockTopLeft.Y);
        var contentRight = Math.Max(
            petTopLeft.X + PetBody.ActualWidth,
            dockTopLeft.X + ActionDock.ActualWidth);
        var contentBottom = Math.Max(
            petTopLeft.Y + PetBody.ActualHeight,
            dockTopLeft.Y + ActionDock.ActualHeight);

        var position = WindowPlacement.ClampToDesktop(
            requestedLeft,
            requestedTop,
            new Rect(contentLeft, contentTop, contentRight - contentLeft, contentBottom - contentTop),
            new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight));

        Left = position.X;
        Top = position.Y;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideStandalonePet(persistVisibility: true);
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        AppLog.Write("Application closed.");
        _requestCancellation?.Cancel();
        _hotKey?.Dispose();
        _ocrHotKey?.Dispose();
        _togglePetHotKey?.Dispose();
        _trayIcon?.Dispose();
        _screenWatchTimer.Stop();
        _windowWatchTimer.Stop();
        _commentBubbleTimer.Stop();
        _interactionAnimationTimer.Stop();
        _pointerLookTimer.Stop();
        _statusChipTimer.Stop();
        _idleActionTimer.Stop();
        ComposerPopup.IsOpen = false;
        _activityMonitor.Dispose();
        _lifetimeMonitor.Dispose();
        _codex.Dispose();
        _petController.Dispose();
    }

    private enum ScreenCommentTrigger
    {
        Manual,
        Periodic,
        WindowChange
    }
}
