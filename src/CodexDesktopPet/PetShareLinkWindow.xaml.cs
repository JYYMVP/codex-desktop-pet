using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace CodexDesktopPet;

public partial class PetShareLinkWindow : Window
{
    public PetShareLinkWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ShareUrlBox.Focus();
    }

    internal string ShareUrl => ShareUrlBox.Text.Trim();

    private void Adopt_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ShareUrlBox.Text))
        {
            MessageBox.Show(this, "请粘贴分享链接。", "从公网链接领养",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
