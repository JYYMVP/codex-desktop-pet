using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CodexDesktopPet;

public partial class OfficialPetsBubbleWindow : Window
{
    private readonly DispatcherTimer _dismissTimer = new()
    {
        Interval = TimeSpan.FromSeconds(9)
    };

    public OfficialPetsBubbleWindow()
    {
        InitializeComponent();
        _dismissTimer.Tick += (_, _) => HideBubble();
    }

    public void ShowComment(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return;

        _dismissTimer.Stop();
        CommentText.Text = comment.Trim();
        if (!IsVisible)
            Show();
        UpdateLayout();

        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left + 12, workArea.Right - ActualWidth - 24);
        Top = Math.Max(workArea.Top + 12, workArea.Bottom - ActualHeight - 88);
        _dismissTimer.Start();
    }

    public void HideBubble()
    {
        _dismissTimer.Stop();
        Hide();
    }

    public void Shutdown()
    {
        _dismissTimer.Stop();
        Close();
    }

    private void Bubble_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HideBubble();
        e.Handled = true;
    }
}
