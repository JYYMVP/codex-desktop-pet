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
            {
                _timer.Interval = TimeSpan.FromMilliseconds(240);
                RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
            }
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
        if (manifest.CanvasSize is < 1 or > 208)
            throw new InvalidDataException("CanvasSize 必须在 1 到 208 之间。");
        RenderOptions.SetBitmapScalingMode(_image, manifest.PixelArt
            ? BitmapScalingMode.NearestNeighbor
            : BitmapScalingMode.HighQuality);

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
                if (bitmap.PixelWidth > manifest.CanvasSize || bitmap.PixelHeight > manifest.CanvasSize)
                    throw new InvalidDataException(
                        $"帧 {relative} 超过 {manifest.CanvasSize}×{manifest.CanvasSize} 像素。");
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
            PetState.Thinking => MediaColor.FromRgb(104, 233, 255),
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

        // A compact water-drop silhouette inspired by the user's reference:
        // pointed crown, broad cheeks and a softly rounded base.
        silhouette[16, 2] = true;
        for (var y = 3; y <= 12; y++)
        {
            var halfWidth = Math.Max(1, (int)Math.Round((y - 1) * 0.72));
            for (var x = 16 - halfWidth; x <= 16 + halfWidth; x++)
                silhouette[x, y] = true;
        }
        for (var y = 9; y <= 24; y++)
        for (var x = 6; x <= 26; x++)
        {
            var dx = (x - 16.0) / 10.1;
            var dy = (y - 16.6) / 8.7;
            if (dx * dx + dy * dy <= 1)
                silhouette[x, y] = true;
        }

        // Detached boot-like feet and tiny animated arms match the reference
        // without making the face too busy at 32 logical pixels.
        for (var y = 26; y <= 28; y++)
        for (var x = 10; x <= 13; x++)
            silhouette[x, y] = y < 28 || x <= 12;
        for (var y = 26; y <= 28; y++)
        for (var x = 20; x <= 23; x++)
            silhouette[x, y] = y < 28 || x >= 21;

        var leftArmY = 17 + (frame switch { 1 => -1, 2 => 1, _ => 0 });
        silhouette[6, leftArmY] = true;
        silhouette[5, leftArmY + 1] = true;
        silhouette[4, leftArmY + 2] = true;
        silhouette[5, leftArmY + 2] = true;
        silhouette[6, leftArmY + 3] = true;
        var rightArmY = 17 + (frame switch { 2 => -1, 3 => 1, _ => 0 });
        silhouette[26, rightArmY] = true;
        silhouette[27, rightArmY + 1] = true;
        silhouette[28, rightArmY + 2] = true;
        silhouette[27, rightArmY + 2] = true;
        silhouette[26, rightArmY + 3] = true;

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
                : y >= 22 || y >= 26
                    ? shadow
                    : accent;
            Pixel(x, y, fill);
        }

        // The large off-centre glossy patch is the defining feature of the
        // reference mascot. Its stepped edge keeps it crisp at native size.
        var gloss = Mix(highlight, MediaColor.FromRgb(255, 255, 255), 0.34);
        Pixel(14, 5, gloss); Pixel(15, 5, gloss);
        for (var y = 6; y <= 9; y++)
        for (var x = 12; x <= 16 - (y > 7 ? 1 : 0); x++)
            Pixel(x, y, gloss);
        Pixel(11, 9, gloss); Pixel(12, 10, gloss); Pixel(13, 10, gloss);
        Pixel(11, 11, highlight); Pixel(12, 11, highlight);
        Pixel(6, leftArmY + 1, highlight);

        // Small vertical eyes, blush, and a tiny smile keep the face closer to
        // the supplied reference than the previous oversized-eye design.
        var blink = state == PetState.Thinking && frame == 2;
        var cheek = state == PetState.Error
            ? MediaColor.FromRgb(255, 116, 142)
            : MediaColor.FromRgb(255, 158, 184);
        if (state == PetState.Error)
        {
            Pixel(12, 15, outline); Pixel(13, 16, outline);
            Pixel(13, 15, outline); Pixel(12, 16, outline);
            Pixel(20, 15, outline); Pixel(21, 16, outline);
            Pixel(21, 15, outline); Pixel(20, 16, outline);
        }
        else if (blink)
        {
            Pixel(12, 16, outline); Pixel(13, 16, outline);
            Pixel(20, 16, outline); Pixel(21, 16, outline);
        }
        else
        {
            Pixel(12, 15, eyeWhite); Pixel(13, 15, outline);
            Pixel(12, 16, outline); Pixel(13, 16, outline);
            Pixel(12, 17, outline); Pixel(13, 17, outline);

            Pixel(20, 15, eyeWhite); Pixel(21, 15, outline);
            Pixel(20, 16, outline); Pixel(21, 16, outline);
            Pixel(20, 17, outline); Pixel(21, 17, outline);
        }
        Pixel(8, 19, cheek); Pixel(9, 19, cheek); Pixel(10, 19, cheek);
        Pixel(8, 20, cheek); Pixel(9, 20, cheek);
        Pixel(23, 19, cheek); Pixel(24, 19, cheek); Pixel(25, 19, cheek);
        Pixel(24, 20, cheek); Pixel(25, 20, cheek);

        if (state == PetState.Error)
        {
            Pixel(15, 21, outline); Pixel(16, 20, outline); Pixel(17, 21, outline);
        }
        else if (state == PetState.NeedsInput)
        {
            Pixel(15, 20, outline); Pixel(16, 20, outline); Pixel(17, 20, outline);
        }
        else
        {
            Pixel(16, 20, outline); Pixel(17, 20, outline);
        }

        DrawStateAccent(state, frame, Pixel, accent, highlight, outline);
        if (state == PetState.Thinking)
            DrawWorkLaptop(frame, Pixel, outline, shadow, highlight);

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

    private static void DrawWorkLaptop(
        int frame,
        Action<int, int, MediaColor> pixel,
        MediaColor outline,
        MediaColor shadow,
        MediaColor highlight)
    {
        var screen = MediaColor.FromRgb(49, 70, 108);
        var screenGlow = MediaColor.FromRgb(123, 183, 255);

        for (var y = 20; y <= 26; y++)
        for (var x = 5; x <= 16; x++)
            pixel(x, y, x is 5 or 16 || y is 20 or 26 ? outline : screen);

        pixel(10, 22, screenGlow);
        pixel(9, 23, screenGlow);
        pixel(10, 23, highlight);
        pixel(11, 23, screenGlow);

        for (var x = 4; x <= 18; x++)
            pixel(x, 27, x is 4 or 18 ? outline : shadow);
        for (var x = 7; x <= 15; x++)
            pixel(x, 28, x is 7 or 15 ? outline : shadow);

        // The right hand alternates between two keys while the request runs.
        var handX = 17 + frame % 2;
        pixel(handX, 24, highlight);
        pixel(handX, 25, outline);
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
        public bool PixelArt { get; init; } = true;
        public Dictionary<string, string[]> States { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
