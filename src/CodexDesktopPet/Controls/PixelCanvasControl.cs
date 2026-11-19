using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfBrush = System.Windows.Media.Brush;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace CodexDesktopPet.Controls;

internal enum PixelTool
{
    Pencil,
    Eraser,
    Fill
}

internal sealed class PixelCanvasControl : FrameworkElement
{
    private WpfColor?[,] _pixels = new WpfColor?[32, 32];
    private bool _drawing;
    private (int X, int Y)? _lastCell;

    public int CanvasSize => _pixels.GetLength(0);
    public PixelTool Tool { get; set; } = PixelTool.Pencil;
    public WpfColor SelectedColor { get; set; } = WpfColor.FromRgb(104, 233, 255);
    public bool HasArtwork => _pixels.Cast<WpfColor?>().Any(color => color.HasValue);

    public event EventHandler? PixelsChanged;

    public PixelCanvasControl()
    {
        Focusable = true;
        Cursor = WpfCursors.Cross;
        SnapsToDevicePixels = true;
    }

    public void ResizeCanvas(int newSize)
    {
        newSize = Math.Clamp(newSize, 8, 64);
        if (newSize == CanvasSize)
            return;

        var resized = new WpfColor?[newSize, newSize];
        var offset = (newSize - CanvasSize) / 2;
        for (var y = 0; y < CanvasSize; y++)
        for (var x = 0; x < CanvasSize; x++)
        {
            var targetX = x + offset;
            var targetY = y + offset;
            if (targetX >= 0 && targetY >= 0 && targetX < newSize && targetY < newSize)
                resized[targetX, targetY] = _pixels[x, y];
        }

        _pixels = resized;
        NotifyPixelsChanged();
    }

    public void Clear()
    {
        _pixels = new WpfColor?[CanvasSize, CanvasSize];
        NotifyPixelsChanged();
    }

    public BitmapSource ExportBitmap()
    {
        var size = CanvasSize;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var color = _pixels[x, y];
            if (!color.HasValue)
                continue;
            var index = (y * size + x) * 4;
            pixels[index] = color.Value.B;
            pixels[index + 1] = color.Value.G;
            pixels[index + 2] = color.Value.R;
            pixels[index + 3] = color.Value.A;
        }

        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var geometry = GetCanvasGeometry();
        drawingContext.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(18, 18, 20)),
            null, new Rect(0, 0, ActualWidth, ActualHeight));

        var checkerA = new SolidColorBrush(WpfColor.FromRgb(30, 30, 34));
        var checkerB = new SolidColorBrush(WpfColor.FromRgb(39, 39, 44));
        checkerA.Freeze();
        checkerB.Freeze();
        var brushes = new Dictionary<WpfColor, SolidColorBrush>();
        for (var y = 0; y < CanvasSize; y++)
        for (var x = 0; x < CanvasSize; x++)
        {
            var color = _pixels[x, y];
            WpfBrush brush;
            if (color.HasValue)
            {
                if (!brushes.TryGetValue(color.Value, out var pixelBrush))
                {
                    pixelBrush = new SolidColorBrush(color.Value);
                    pixelBrush.Freeze();
                    brushes[color.Value] = pixelBrush;
                }
                brush = pixelBrush;
            }
            else
            {
                brush = (x + y) % 2 == 0 ? checkerA : checkerB;
            }

            drawingContext.DrawRectangle(brush, null, new Rect(
                geometry.Left + x * geometry.Cell,
                geometry.Top + y * geometry.Cell,
                geometry.Cell + 0.25,
                geometry.Cell + 0.25));
        }

        if (geometry.Cell >= 6)
        {
            var gridPen = new WpfPen(new SolidColorBrush(WpfColor.FromArgb(80, 116, 116, 126)), 0.5);
            gridPen.Freeze();
            for (var index = 0; index <= CanvasSize; index++)
            {
                var offset = index * geometry.Cell;
                drawingContext.DrawLine(gridPen,
                    new WpfPoint(geometry.Left + offset, geometry.Top),
                    new WpfPoint(geometry.Left + offset, geometry.Top + geometry.Size));
                drawingContext.DrawLine(gridPen,
                    new WpfPoint(geometry.Left, geometry.Top + offset),
                    new WpfPoint(geometry.Left + geometry.Size, geometry.Top + offset));
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        _drawing = true;
        _lastCell = null;
        ApplyAt(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(WpfMouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_drawing || e.LeftButton != MouseButtonState.Pressed || Tool == PixelTool.Fill)
            return;
        ApplyAt(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _drawing = false;
        _lastCell = null;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ApplyAt(WpfPoint point)
    {
        var cell = PointToCell(point);
        if (cell is null || cell == _lastCell)
            return;
        _lastCell = cell;

        if (Tool == PixelTool.Fill)
            Fill(cell.Value.X, cell.Value.Y);
        else
            _pixels[cell.Value.X, cell.Value.Y] = Tool == PixelTool.Eraser
                ? null
                : SelectedColor;
        NotifyPixelsChanged();
    }

    private void Fill(int startX, int startY)
    {
        var target = _pixels[startX, startY];
        WpfColor? replacement = Tool == PixelTool.Eraser ? null : SelectedColor;
        if (target == replacement)
            return;

        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((startX, startY));
        _pixels[startX, startY] = replacement;
        while (queue.TryDequeue(out var cell))
        {
            foreach (var (x, y) in new[]
                     {
                         (cell.X - 1, cell.Y), (cell.X + 1, cell.Y),
                         (cell.X, cell.Y - 1), (cell.X, cell.Y + 1)
                     })
            {
                if (x < 0 || y < 0 || x >= CanvasSize || y >= CanvasSize ||
                    _pixels[x, y] != target)
                    continue;
                _pixels[x, y] = replacement;
                queue.Enqueue((x, y));
            }
        }
    }

    private (int X, int Y)? PointToCell(WpfPoint point)
    {
        var geometry = GetCanvasGeometry();
        var x = (int)Math.Floor((point.X - geometry.Left) / geometry.Cell);
        var y = (int)Math.Floor((point.Y - geometry.Top) / geometry.Cell);
        return x >= 0 && y >= 0 && x < CanvasSize && y < CanvasSize
            ? (x, y)
            : null;
    }

    private (double Left, double Top, double Size, double Cell) GetCanvasGeometry()
    {
        var size = Math.Max(1, Math.Min(ActualWidth, ActualHeight));
        var cell = size / CanvasSize;
        var renderedSize = cell * CanvasSize;
        return ((ActualWidth - renderedSize) / 2,
            (ActualHeight - renderedSize) / 2,
            renderedSize,
            cell);
    }

    private void NotifyPixelsChanged()
    {
        InvalidateVisual();
        PixelsChanged?.Invoke(this, EventArgs.Empty);
    }
}
