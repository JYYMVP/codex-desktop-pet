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
        var bounds = Forms.SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("无法读取当前桌面尺寸。");

        var captureDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexDesktopPet", "captures");
        Directory.CreateDirectory(captureDirectory);
        var path = Path.Combine(captureDirectory, $"screen-{Guid.NewGuid():N}.png");

        using var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(full))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size,
                CopyPixelOperation.SourceCopy);
        }

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
