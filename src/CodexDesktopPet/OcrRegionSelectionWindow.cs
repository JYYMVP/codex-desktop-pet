using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using IOPath = System.IO.Path;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfImage = System.Windows.Controls.Image;

namespace CodexDesktopPet;

internal sealed class OcrRegionSelectionWindow : Window
{
    private readonly Canvas _selectionCanvas = new();
    private readonly WpfRectangle _selectionRectangle = new()
    {
        Stroke = new SolidColorBrush(WpfColor.FromRgb(104, 233, 255)),
        StrokeThickness = 2,
        Fill = new SolidColorBrush(WpfColor.FromArgb(32, 104, 233, 255)),
        Visibility = Visibility.Collapsed
    };
    private WpfPoint? _startPoint;

    public Rect NormalizedSelection { get; private set; }

    public OcrRegionSelectionWindow(string snapshotPath)
    {
        Title = "框选要识别的区域";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = false;
        Background = WpfBrushes.Black;
        Cursor = WpfCursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(IOPath.GetFullPath(snapshotPath));
        bitmap.EndInit();
        bitmap.Freeze();

        var root = new Grid();
        root.Children.Add(new WpfImage
        {
            Source = bitmap,
            Stretch = Stretch.Fill,
            Opacity = 0.82,
            IsHitTestVisible = false
        });
        root.Children.Add(new Border
        {
            Background = new SolidColorBrush(WpfColor.FromArgb(48, 0, 0, 0)),
            IsHitTestVisible = false
        });
        _selectionCanvas.Children.Add(_selectionRectangle);
        root.Children.Add(_selectionCanvas);
        root.Children.Add(CreateInstruction());
        Content = root;

        PreviewKeyDown += OnPreviewKeyDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += (_, _) => CancelSelection();
    }

    private static Border CreateInstruction() => new()
    {
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 28, 0, 0),
        Padding = new Thickness(16, 9, 16, 9),
        CornerRadius = new CornerRadius(10),
        Background = new SolidColorBrush(WpfColor.FromArgb(232, 24, 24, 26)),
        BorderBrush = new SolidColorBrush(WpfColor.FromRgb(74, 74, 80)),
        BorderThickness = new Thickness(1),
        Child = new TextBlock
        {
            Text = "拖动框选需要识别的区域 · Esc 或右键取消",
            Foreground = WpfBrushes.White,
            FontFamily = new WpfFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14
        }
    };

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        CancelSelection();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = ClampPoint(e.GetPosition(this));
        CaptureMouse();
        UpdateSelection(_startPoint.Value, _startPoint.Value);
        _selectionRectangle.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_startPoint is null || e.LeftButton != MouseButtonState.Pressed)
            return;
        UpdateSelection(_startPoint.Value, ClampPoint(e.GetPosition(this)));
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_startPoint is null)
            return;

        var endPoint = ClampPoint(e.GetPosition(this));
        var selection = Normalize(_startPoint.Value, endPoint);
        _startPoint = null;
        ReleaseMouseCapture();
        if (selection.Width < 12 || selection.Height < 12 ||
            ActualWidth <= 0 || ActualHeight <= 0)
        {
            _selectionRectangle.Visibility = Visibility.Collapsed;
            return;
        }

        NormalizedSelection = new Rect(
            selection.X / ActualWidth,
            selection.Y / ActualHeight,
            selection.Width / ActualWidth,
            selection.Height / ActualHeight);
        DialogResult = true;
        e.Handled = true;
    }

    private void UpdateSelection(WpfPoint start, WpfPoint end)
    {
        var rectangle = Normalize(start, end);
        Canvas.SetLeft(_selectionRectangle, rectangle.X);
        Canvas.SetTop(_selectionRectangle, rectangle.Y);
        _selectionRectangle.Width = rectangle.Width;
        _selectionRectangle.Height = rectangle.Height;
    }

    private WpfPoint ClampPoint(WpfPoint point) => new(
        Math.Clamp(point.X, 0, Math.Max(0, ActualWidth)),
        Math.Clamp(point.Y, 0, Math.Max(0, ActualHeight)));

    private static Rect Normalize(WpfPoint start, WpfPoint end) => new(
        Math.Min(start.X, end.X),
        Math.Min(start.Y, end.Y),
        Math.Abs(start.X - end.X),
        Math.Abs(start.Y - end.Y));

    private void CancelSelection()
    {
        DialogResult = false;
    }
}

