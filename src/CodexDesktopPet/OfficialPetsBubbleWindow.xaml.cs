using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CodexDesktopPet.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace CodexDesktopPet;

public partial class OfficialPetsBubbleWindow : Window
{
    private static readonly MediaBrush AnswerBrush = FrozenBrush(104, 233, 255);
    private static readonly MediaBrush CommentBrush = FrozenBrush(187, 139, 255);
    private static readonly MediaBrush ErrorBrush = FrozenBrush(255, 122, 146);
    private readonly DispatcherTimer _dismissTimer = new()
    {
        Interval = TimeSpan.FromSeconds(9)
    };
    private readonly DispatcherTimer _anchorTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(300)
    };
    private readonly DispatcherTimer _workingTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(380)
    };
    private string _content = string.Empty;
    private int _workingFrame;
    private bool? _lastAnchored;

    public double AnchorVerticalOffset { get; init; }
    public bool IsBubbleVisible => IsVisible;

    public OfficialPetsBubbleWindow()
    {
        InitializeComponent();
        _dismissTimer.Tick += (_, _) => HideBubble();
        _anchorTimer.Tick += (_, _) => PositionNearOfficialPet();
        _workingTimer.Tick += (_, _) =>
        {
            _workingFrame = (_workingFrame + 1) % 3;
            MessageText.Text = new string('·', _workingFrame + 1);
        };
    }

    public void ShowWorking()
    {
        _content = string.Empty;
        _dismissTimer.Stop();
        _workingTimer.Stop();
        _workingFrame = 0;
        KindLabel.Text = "问答";
        KindDot.Fill = AnswerBrush;
        BubbleChrome.BorderBrush = AnswerBrush;
        CopyButton.Visibility = Visibility.Collapsed;
        Width = 132;
        MaxHeight = 120;
        MessageText.Text = "·";
        ShowAndAnchor();
        _workingTimer.Start();
    }

    public void ShowAnswer(string answer, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return;

        _content = answer.Trim();
        _dismissTimer.Stop();
        _workingTimer.Stop();
        KindLabel.Text = isError ? "错误" : "问答";
        KindDot.Fill = isError ? ErrorBrush : AnswerBrush;
        BubbleChrome.BorderBrush = isError ? ErrorBrush : AnswerBrush;
        CopyButton.Visibility = Visibility.Visible;
        Width = 380;
        MaxHeight = 440;
        MarkdownRenderer.Render(MessageText, _content);
        ShowAndAnchor();
    }

    public void ShowComment(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return;

        _content = comment.Trim();
        _dismissTimer.Stop();
        _workingTimer.Stop();
        KindLabel.Text = "碎碎念";
        KindDot.Fill = CommentBrush;
        BubbleChrome.BorderBrush = CommentBrush;
        CopyButton.Visibility = Visibility.Collapsed;
        Width = 306;
        MaxHeight = 230;
        MarkdownRenderer.Render(MessageText, _content);
        ShowAndAnchor();
        _dismissTimer.Start();
    }

    public void HideBubble()
    {
        _dismissTimer.Stop();
        _workingTimer.Stop();
        _anchorTimer.Stop();
        Hide();
    }

    public void RestoreBubble()
    {
        if (!string.IsNullOrWhiteSpace(_content))
            ShowAndAnchor();
    }

    public void Shutdown()
    {
        _dismissTimer.Stop();
        _workingTimer.Stop();
        _anchorTimer.Stop();
        Close();
    }

    private void ShowAndAnchor()
    {
        if (!IsVisible)
            Show();
        UpdateLayout();
        PositionNearOfficialPet();
        _anchorTimer.Start();
    }

    private void PositionNearOfficialPet()
    {
        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        if (OfficialPetsQuickChatService.TryGetPetsAnchor(out var anchor))
        {
            if (_lastAnchored != true)
                AppLog.Write($"Official Pets bubble anchored. Bounds={anchor}");
            _lastAnchored = true;
            var dpi = VisualTreeHelper.GetDpi(this);
            var left = anchor.Left / dpi.DpiScaleX;
            var top = anchor.Top / dpi.DpiScaleY;
            var width = anchor.Width / dpi.DpiScaleX;
            var height = anchor.Height / dpi.DpiScaleY;
            var candidateLeft = left - ActualWidth - 10;
            if (candidateLeft < workArea.Left + 8)
                candidateLeft = left + width + 10;
            Left = Math.Clamp(candidateLeft, workArea.Left + 8,
                Math.Max(workArea.Left + 8, workArea.Right - ActualWidth - 8));
            Top = Math.Clamp(top + height - ActualHeight + AnchorVerticalOffset, workArea.Top + 8,
                Math.Max(workArea.Top + 8, workArea.Bottom - ActualHeight - 8));
            return;
        }

        if (_lastAnchored != false)
            AppLog.Write("Official Pets bubble anchor unavailable; using work-area fallback.");
        _lastAnchored = false;
        Left = Math.Max(workArea.Left + 12, workArea.Right - ActualWidth - 24);
        Top = Math.Clamp(workArea.Bottom - ActualHeight - 88 + AnchorVerticalOffset,
            workArea.Top + 8, Math.Max(workArea.Top + 8, workArea.Bottom - ActualHeight - 8));
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_content))
            System.Windows.Clipboard.SetText(_content);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => HideBubble();

    private static MediaBrush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
