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
    private GlobalHotKeyManager? _hotKey;
    private Forms.NotifyIcon? _trayIcon;
    private CancellationTokenSource? _requestCancellation;
    private bool _allowClose;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        _codex = new CodexCliService(_settings);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.WorkArea.Right - ActualWidth - 18;
        Top = SystemParameters.WorkArea.Bottom - ActualHeight - 18;

        CreateTrayIcon();
        SetPetState(PetState.Idle);
        StartIdleAnimation();

        try
        {
            _hotKey = new GlobalHotKeyManager(this,
                ModifierKeys.Control | ModifierKeys.Alt, Key.Q);
            _hotKey.Pressed += HotKey_Pressed;
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, PetState.Error);
        }
    }

    private async void HotKey_Pressed(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        _busy = true;
        AnswerPanel.Visibility = Visibility.Collapsed;
        SetPetState(PetState.Capturing);

        try
        {
            var selectedText = await _selectionCapture.CaptureAsync();
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                ShowMessage("没有读到选中的文字。请先选中一段文字，再按 Ctrl+Alt+Q。",
                    PetState.NeedsInput);
                return;
            }

            SetPetState(PetState.Thinking);
            _requestCancellation = new CancellationTokenSource();
            var answer = await _codex.AskAsync(selectedText, _requestCancellation.Token);
            ShowMessage(answer, PetState.Ready);
        }
        catch (Exception ex)
        {
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
        LeftEye.Fill = RightEye.Fill = new SolidColorBrush(color);
        GlowRing.Stroke = new SolidColorBrush(color) { Opacity = 0.65 };
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

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
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
        _requestCancellation?.Cancel();
        _hotKey?.Dispose();
        _trayIcon?.Dispose();
    }
}
