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
            _frames = string.IsNullOrWhiteSpace(packPath)
                ? CreateBuiltInFrames()
                : LoadPack(packPath);
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
            result[state] = [CreateBuiltInFrame(state, 0), CreateBuiltInFrame(state, 1)];
        return result;
    }

    private static ImageSource CreateBuiltInFrame(PetState state, int frame)
    {
        const int size = 64;
        const int cell = 4;
        var pixels = new byte[size * size * 4];
        var accent = state switch
        {
            PetState.Thinking => MediaColor.FromRgb(187, 139, 255),
            PetState.Capturing => MediaColor.FromRgb(255, 210, 112),
            PetState.Ready => MediaColor.FromRgb(104, 233, 255),
            PetState.NeedsInput => MediaColor.FromRgb(255, 190, 90),
            PetState.Error => MediaColor.FromRgb(255, 105, 135),
            _ => MediaColor.FromRgb(104, 233, 255)
        };
        var dark = MediaColor.FromRgb(22, 31, 48);
        var light = MediaColor.FromRgb(230, 252, 255);

        void Cell(int x, int y, MediaColor color)
        {
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

        // Tail and fins move by one logical pixel between frames.
        var tailShift = frame == 0 ? 0 : 1;
        Cell(2, 6 + tailShift, accent); Cell(3, 7, accent);
        Cell(2, 8 - tailShift, accent); Cell(3, 8, accent);
        Cell(7, 12, accent); Cell(8, 12, accent);

        var rows = new[] { (5, 10), (4, 11), (4, 12), (4, 12), (4, 11), (5, 10) };
        for (var row = 0; row < rows.Length; row++)
        {
            var y = 5 + row;
            var (start, end) = rows[row];
            for (var x = start; x <= end; x++)
                Cell(x, y, x == start || x == end ? dark : accent);
        }

        // Face and state expression.
        var blink = state == PetState.Thinking && frame == 1;
        Cell(7, 7, blink ? dark : light);
        Cell(10, 7, blink ? dark : light);
        if (!blink) { Cell(7, 8, dark); Cell(10, 8, dark); }
        if (state == PetState.Error)
        {
            Cell(8, 10, dark); Cell(9, 9, dark); Cell(10, 10, dark);
        }
        else if (state == PetState.NeedsInput)
        {
            Cell(8, 10, dark); Cell(9, 10, dark);
        }
        else
        {
            Cell(8, 9, dark); Cell(9, 10, dark); Cell(10, 9, dark);
        }

        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bitmap.Freeze();
        return bitmap;
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
