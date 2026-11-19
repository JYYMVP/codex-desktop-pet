using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexDesktopPet.Models;
using MediaColor = System.Windows.Media.Color;

namespace CodexDesktopPet.Services;

internal sealed class PixelPetController : IDisposable
{
    private readonly System.Windows.Controls.Image _image;
    private readonly DispatcherTimer _timer = new();
    private Dictionary<PetState, List<ImageSource>> _frames = [];
    private PetState _state = PetState.Idle;
    private int _frameIndex;

    public PixelPetController(System.Windows.Controls.Image image, string packPath)
    {
        _image = image;
        _timer.Tick += (_, _) => AdvanceFrame();
        Reload(packPath);
    }

    public void Reload(string packPath)
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(360);
        try
        {
            var useBuiltInPet = string.IsNullOrWhiteSpace(packPath);
            _frames = useBuiltInPet ? CreateBuiltInFrames() : LoadPack(packPath);
            if (useBuiltInPet)
                _timer.Interval = TimeSpan.FromMilliseconds(240);
            AppLog.Write(string.IsNullOrWhiteSpace(packPath)
                ? "Loaded built-in 64x64 pixel pet."
                : $"Loaded pixel pet pack: {Path.GetFullPath(packPath)}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Pixel pet pack failed; using built-in pet: {ex.Message}");
            _frames = CreateBuiltInFrames();
        }

        _frameIndex = 0;
        _timer.Start();
        Render();
    }

    public void SetState(PetState state)
    {
        _state = state;
        _frameIndex = 0;
        Render();
    }

    private void AdvanceFrame()
    {
        _frameIndex++;
        Render();
    }

    private void Render()
    {
        if (!_frames.TryGetValue(_state, out var frames) || frames.Count == 0)
            frames = _frames[PetState.Idle];
        _image.Source = frames[_frameIndex % frames.Count];
    }

    private Dictionary<PetState, List<ImageSource>> LoadPack(string configuredPath)
    {
        var manifestPath = Directory.Exists(configuredPath)
            ? Path.Combine(configuredPath, "pet.json")
            : configuredPath;
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var manifest = JsonSerializer.Deserialize<PetPackManifest>(
                           File.ReadAllText(manifestPath),
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? throw new InvalidDataException("pet.json 内容无效。");
        if (manifest.CanvasSize is < 1 or > 64)
            throw new InvalidDataException("CanvasSize 必须在 1 到 64 之间。");

        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(manifest.FrameDurationMs, 80, 2000));
        var result = new Dictionary<PetState, List<ImageSource>>();
        foreach (var (name, files) in manifest.States)
        {
            if (!Enum.TryParse<PetState>(name, true, out var state) || files.Length == 0)
                continue;

            var loaded = new List<ImageSource>();
            foreach (var relative in files)
            {
                var path = Path.GetFullPath(Path.Combine(root, relative));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("帧路径不能离开宠物包目录。");
                if (!string.Equals(Path.GetExtension(path), ".png",
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("宠物帧只支持 PNG。");

                using var stream = File.OpenRead(path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                if (bitmap.PixelWidth > 64 || bitmap.PixelHeight > 64)
                    throw new InvalidDataException($"帧 {relative} 超过 64×64 像素。");
                loaded.Add(bitmap);
            }
            result[state] = loaded;
        }

        if (!result.ContainsKey(PetState.Idle))
            throw new InvalidDataException("宠物包至少需要 idle 状态。");
        return result;
    }

    private static Dictionary<PetState, List<ImageSource>> CreateBuiltInFrames()
    {
        var result = new Dictionary<PetState, List<ImageSource>>();
        foreach (var state in Enum.GetValues<PetState>())
            result[state] = Enumerable.Range(0, 4)
                .Select(frame => CreateBuiltInFrame(state, frame))
                .ToList();
        return result;
    }

    private static ImageSource CreateBuiltInFrame(PetState state, int frame)
    {
        const int size = 64;
        const int logicalSize = 32;
        const int cell = 2;
        var pixels = new byte[size * size * 4];
        var accent = state switch
        {
            PetState.Thinking => MediaColor.FromRgb(187, 139, 255),
            PetState.Capturing => MediaColor.FromRgb(111, 207, 255),
            PetState.Ready => MediaColor.FromRgb(104, 233, 255),
            PetState.NeedsInput => MediaColor.FromRgb(255, 190, 90),
            PetState.Error => MediaColor.FromRgb(255, 105, 135),
            _ => MediaColor.FromRgb(104, 233, 255)
        };
        var outline = MediaColor.FromRgb(18, 27, 43);
        var shadow = Mix(accent, outline, 0.38);
        var highlight = Mix(accent, MediaColor.FromRgb(244, 255, 255), 0.56);
        var eyeWhite = MediaColor.FromRgb(244, 255, 255);
        var silhouette = new bool[logicalSize, logicalSize];

        void Pixel(int x, int y, MediaColor color)
        {
            if (x is < 0 or >= logicalSize || y is < 0 or >= logicalSize)
                return;
            for (var py = y * cell; py < (y + 1) * cell; py++)
            for (var px = x * cell; px < (x + 1) * cell; px++)
            {
                var i = (py * size + px) * 4;
                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = 255;
            }
        }

        // A round sprout mascot is clearer and friendlier than a tiny side-view
        // animal. The body is intentionally almost symmetrical so expressions
        // stay readable at the 32x32 logical resolution.
        for (var y = 8; y <= 25; y++)
        for (var x = 6; x <= 26; x++)
        {
            var dx = (x - 16.0) / 10.0;
            var dy = (y - 16.5) / 8.8;
            if (dx * dx + dy * dy <= 1)
                silhouette[x, y] = true;
        }

        // A tiny sprout, two stubby feet, and one waving arm give it a mascot
        // silhouette without relying on high-resolution details.
        silhouette[16, 5] = silhouette[16, 6] = silhouette[16, 7] = true;
        silhouette[13, 4] = silhouette[14, 4] = silhouette[14, 5] = true;
        silhouette[15, 5] = silhouette[15, 6] = true;
        silhouette[17, 3] = silhouette[18, 3] = silhouette[18, 4] = true;
        silhouette[17, 4] = silhouette[17, 5] = silhouette[17, 6] = true;
        for (var x = 10; x <= 14; x++)
            silhouette[x, 25 + (x is 11 or 12 or 13 ? 1 : 0)] = true;
        for (var x = 18; x <= 22; x++)
            silhouette[x, 25 + (x is 19 or 20 or 21 ? 1 : 0)] = true;

        var leftArmTop = 15 + (frame switch { 1 => -2, 2 => -1, _ => 0 });
        silhouette[5, leftArmTop] = true;
        silhouette[4, leftArmTop + 1] = true;
        silhouette[4, leftArmTop + 2] = true;
        silhouette[5, leftArmTop + 3] = true;
        silhouette[6, leftArmTop + 3] = true;
        silhouette[26, 16] = silhouette[27, 17] = silhouette[27, 18] = true;
        silhouette[26, 19] = true;

        for (var y = 0; y < logicalSize; y++)
        for (var x = 0; x < logicalSize; x++)
        {
            if (!silhouette[x, y])
                continue;
            var boundary = false;
            for (var offsetY = -1; offsetY <= 1 && !boundary; offsetY++)
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (Math.Abs(offsetX) + Math.Abs(offsetY) != 1)
                    continue;
                var neighborX = x + offsetX;
                var neighborY = y + offsetY;
                if (neighborX is < 0 or >= logicalSize ||
                    neighborY is < 0 or >= logicalSize ||
                    !silhouette[neighborX, neighborY])
                {
                    boundary = true;
                    break;
                }
            }

            var fill = boundary
                ? outline
                : y >= 21
                    ? shadow
                    : y <= 11 && x is >= 9 and <= 18
                        ? highlight
                        : accent;
            Pixel(x, y, fill);
        }

        // Soft belly, glossy forehead, and rosy cheeks keep the sprite from
        // reading as a flat colored circle.
        var belly = Mix(accent, MediaColor.FromRgb(248, 255, 255), 0.42);
        for (var y = 20; y <= 22; y++)
        for (var x = 12 + (y - 20); x <= 20 - (y - 20); x++)
            Pixel(x, y, belly);
        Pixel(10, 10, highlight); Pixel(11, 9, highlight); Pixel(12, 9, highlight);
        Pixel(13, 10, highlight); Pixel(5, leftArmTop + 1, highlight);
        var sprout = MediaColor.FromRgb(139, 232, 156);
        Pixel(14, 4, sprout); Pixel(14, 5, sprout); Pixel(15, 5, sprout);
        Pixel(17, 3, sprout); Pixel(18, 3, sprout); Pixel(17, 4, sprout);
        Pixel(16, 6, sprout);

        // Two oversized eyes, blush, and a tiny mouth carry most of the charm.
        var blink = state == PetState.Thinking && frame == 2;
        var cheek = state == PetState.Error
            ? MediaColor.FromRgb(255, 116, 142)
            : MediaColor.FromRgb(255, 158, 184);
        if (state == PetState.Error)
        {
            Pixel(11, 13, outline); Pixel(13, 15, outline);
            Pixel(13, 13, outline); Pixel(11, 15, outline);
            Pixel(19, 13, outline); Pixel(21, 15, outline);
            Pixel(21, 13, outline); Pixel(19, 15, outline);
        }
        else if (blink)
        {
            Pixel(11, 14, outline); Pixel(12, 14, outline); Pixel(13, 14, outline);
            Pixel(19, 14, outline); Pixel(20, 14, outline); Pixel(21, 14, outline);
        }
        else
        {
            Pixel(11, 12, outline); Pixel(12, 12, outline); Pixel(13, 12, outline);
            Pixel(10, 13, outline); Pixel(11, 13, eyeWhite); Pixel(12, 13, eyeWhite); Pixel(13, 13, outline);
            Pixel(10, 14, outline); Pixel(11, 14, eyeWhite); Pixel(12, 14, outline); Pixel(13, 14, outline);
            Pixel(11, 15, outline); Pixel(12, 15, outline); Pixel(13, 15, outline);

            Pixel(19, 12, outline); Pixel(20, 12, outline); Pixel(21, 12, outline);
            Pixel(19, 13, outline); Pixel(20, 13, eyeWhite); Pixel(21, 13, eyeWhite); Pixel(22, 13, outline);
            Pixel(19, 14, outline); Pixel(20, 14, outline); Pixel(21, 14, eyeWhite); Pixel(22, 14, outline);
            Pixel(19, 15, outline); Pixel(20, 15, outline); Pixel(21, 15, outline);
        }
        Pixel(8, 18, cheek); Pixel(9, 18, cheek);
        Pixel(23, 18, cheek); Pixel(24, 18, cheek);

        if (state == PetState.Error)
        {
            Pixel(15, 20, outline); Pixel(16, 19, outline); Pixel(17, 20, outline);
        }
        else if (state == PetState.NeedsInput)
        {
            Pixel(15, 19, outline); Pixel(16, 19, outline); Pixel(17, 19, outline);
        }
        else
        {
            Pixel(15, 18, outline); Pixel(16, 19, outline); Pixel(17, 18, outline);
        }

        DrawStateAccent(state, frame, Pixel, accent, highlight, outline);

        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawStateAccent(
        PetState state,
        int frame,
        Action<int, int, MediaColor> pixel,
        MediaColor accent,
        MediaColor highlight,
        MediaColor outline)
    {
        var drift = frame % 3;
        switch (state)
        {
            case PetState.Thinking:
                pixel(27, 9 - drift, highlight);
                pixel(29, 7 - drift, accent);
                pixel(30, 5 - drift, highlight);
                break;
            case PetState.Capturing:
                pixel(29, 11, accent);
                pixel(30, 13, highlight);
                pixel(29, 15, accent);
                break;
            case PetState.Ready:
                pixel(29, 7, highlight);
                pixel(28, 8, highlight);
                pixel(29, 8, highlight);
                pixel(30, 8, highlight);
                pixel(29, 9, highlight);
                break;
            case PetState.NeedsInput:
                pixel(29, 8, accent); pixel(30, 8, accent);
                pixel(30, 9, accent); pixel(29, 10, accent);
                pixel(29, 12, accent);
                break;
            case PetState.Error:
                pixel(29, 7, accent); pixel(29, 8, accent);
                pixel(29, 9, accent); pixel(29, 11, outline);
                break;
            default:
                pixel(28 + frame % 2, 8 - frame % 2, highlight);
                pixel(30, 5 + frame % 2, highlight);
                break;
        }
    }

    private static MediaColor Mix(MediaColor first, MediaColor second, double secondWeight)
    {
        var weight = Math.Clamp(secondWeight, 0, 1);
        byte Blend(byte a, byte b) => (byte)Math.Round(a * (1 - weight) + b * weight);
        return MediaColor.FromRgb(
            Blend(first.R, second.R),
            Blend(first.G, second.G),
            Blend(first.B, second.B));
    }

    public void Dispose() => _timer.Stop();

    private sealed class PetPackManifest
    {
        public int CanvasSize { get; init; } = 64;
        public int FrameDurationMs { get; init; } = 360;
        public Dictionary<string, string[]> States { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
