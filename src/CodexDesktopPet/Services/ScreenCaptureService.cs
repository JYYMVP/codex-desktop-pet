using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Forms = System.Windows.Forms;

namespace CodexDesktopPet.Services;

internal sealed class ScreenCaptureService
{
    public string CaptureDesktop()
    {
        using var full = CaptureDesktopBitmap();
        var path = CreateCapturePath("screen");

        const int maxWidth = 1280;
        const int maxHeight = 800;
        var scale = Math.Min(1d, Math.Min((double)maxWidth / full.Width,
            (double)maxHeight / full.Height));
        if (scale >= 1d)
        {
            full.Save(path, ImageFormat.Png);
            return path;
        }

        var width = Math.Max(1, (int)Math.Round(full.Width * scale));
        var height = Math.Max(1, (int)Math.Round(full.Height * scale));
        using var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(full, new Rectangle(0, 0, width, height));
        }
        resized.Save(path, ImageFormat.Png);
        return path;
    }

    public string CaptureDesktopSnapshot()
    {
        using var bitmap = CaptureDesktopBitmap();
        var path = CreateCapturePath("ocr-source");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    public string CropSnapshot(string snapshotPath, System.Windows.Rect normalizedSelection)
    {
        using var snapshot = new Bitmap(snapshotPath);
        var x = Math.Clamp((int)Math.Floor(normalizedSelection.X * snapshot.Width),
            0, snapshot.Width - 1);
        var y = Math.Clamp((int)Math.Floor(normalizedSelection.Y * snapshot.Height),
            0, snapshot.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(normalizedSelection.Right * snapshot.Width),
            x + 1, snapshot.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(normalizedSelection.Bottom * snapshot.Height),
            y + 1, snapshot.Height);
        var rectangle = new Rectangle(x, y, right - x, bottom - y);
        using var region = snapshot.Clone(rectangle, PixelFormat.Format32bppArgb);
        var path = CreateCapturePath("ocr-region");
        region.Save(path, ImageFormat.Png);
        return path;
    }

    private static Bitmap CaptureDesktopBitmap()
    {
        var bounds = Forms.SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("无法读取当前桌面尺寸。");

        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size,
                CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static string CreateCapturePath(string prefix)
    {
        var captureDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexDesktopPet", "captures");
        Directory.CreateDirectory(captureDirectory);
        return Path.Combine(captureDirectory, $"{prefix}-{Guid.NewGuid():N}.png");
    }

    public static void DeleteCapture(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Screen capture cleanup failed: {ex.Message}");
        }
    }
}
