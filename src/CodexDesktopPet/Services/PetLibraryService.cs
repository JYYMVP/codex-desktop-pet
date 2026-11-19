using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexDesktopPet.Models;

namespace CodexDesktopPet.Services;

internal sealed class PetLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string LibraryRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexDesktopPet", "pets");

    public PetLibraryService() => Directory.CreateDirectory(LibraryRoot);

    public IReadOnlyList<PetLibraryItem> ListPets()
    {
        var result = new List<PetLibraryItem>
        {
            new("default", "水滴小精灵", "Codex Desktop Pet 内置桌宠", string.Empty, true)
        };

        foreach (var directory in Directory.EnumerateDirectories(LibraryRoot))
        {
            var manifestPath = Path.Combine(directory, "pet.json");
            try
            {
                var manifest = ReadManifest(manifestPath);
                result.Add(new PetLibraryItem(
                    string.IsNullOrWhiteSpace(manifest.Id) ? Path.GetFileName(directory) : manifest.Id,
                    string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileName(directory) : manifest.Name,
                    manifest.Description ?? string.Empty,
                    directory));
            }
            catch (Exception ex)
            {
                AppLog.Write($"Skipped invalid library pet {directory}: {ex.Message}");
            }
        }

        return result.OrderByDescending(item => item.IsBuiltIn)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public PetValidationResult Validate(string configuredPath)
    {
        var errors = new List<string>();
        try
        {
            var manifestPath = ResolveManifestPath(configuredPath);
            var root = Path.GetDirectoryName(manifestPath)!;
            var manifest = ReadManifest(manifestPath);
            if (manifest.CanvasSize is < 1 or > 64)
                errors.Add("canvasSize 必须在 1 到 64 之间。");
            if (!manifest.States.Any(pair =>
                    pair.Key.Equals("idle", StringComparison.OrdinalIgnoreCase) && pair.Value.Length > 0))
                errors.Add("至少需要一个 idle 动画帧。");

            foreach (var (state, files) in manifest.States)
            {
                if (files.Length == 0)
                {
                    errors.Add($"状态 {state} 没有动画帧。");
                    continue;
                }

                foreach (var relative in files)
                    ValidateFrame(root, relative, errors);
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        return errors.Count == 0
            ? PetValidationResult.Success
            : new PetValidationResult(false, errors.Distinct().ToArray());
    }

    public PetLibraryItem Import(string sourcePath)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "CodexDesktopPet", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            string packRoot;
            if (string.Equals(Path.GetExtension(sourcePath), ".codexpet", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(sourcePath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                ExtractArchiveSafely(sourcePath, temporaryRoot);
                packRoot = FindPackRoot(temporaryRoot);
            }
            else
            {
                var manifestPath = ResolveManifestPath(sourcePath);
                packRoot = Path.GetDirectoryName(manifestPath)!;
            }

            var validation = Validate(packRoot);
            if (!validation.IsValid)
                throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));

            var manifest = ReadManifest(Path.Combine(packRoot, "pet.json"));
            var id = NormalizeId(manifest.Id);
            if (string.IsNullOrWhiteSpace(id))
                id = $"pet-{Guid.NewGuid():N}";
            var destination = Path.Combine(LibraryRoot, id);
            if (Directory.Exists(destination))
                throw new IOException($"宠物 ID“{id}”已经存在。请先删除旧版本，或修改 pet.json 中的 id。");

            try
            {
                CopyDirectory(packRoot, destination);
                manifest.Id = id;
                manifest.Name = string.IsNullOrWhiteSpace(manifest.Name) ? id : manifest.Name.Trim();
                WriteManifest(destination, manifest);
            }
            catch
            {
                if (Directory.Exists(destination))
                    Directory.Delete(destination, true);
                throw;
            }
            AppLog.Write($"Imported pet {id} from {sourcePath}");
            return new PetLibraryItem(id, manifest.Name, manifest.Description ?? string.Empty, destination);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, true);
            }
            catch (Exception ex)
            {
                AppLog.Write($"Temporary pet import cleanup failed: {ex.Message}");
            }
        }
    }

    public PetLibraryItem CreateStaticPet(string name, string description, int canvasSize,
        BitmapSource bitmap)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("请给宠物起一个名字。");
        if (canvasSize is < 8 or > 64 || bitmap.PixelWidth != canvasSize || bitmap.PixelHeight != canvasSize)
            throw new InvalidDataException("画布必须是 8×8 到 64×64 的正方形。");

        var baseId = NormalizeId(name);
        if (string.IsNullOrWhiteSpace(baseId))
            baseId = "pixel-pet";
        baseId = baseId[..Math.Min(baseId.Length, 32)];
        var id = $"{baseId}-{Guid.NewGuid():N}"[..(baseId.Length + 9)];
        var destination = Path.Combine(LibraryRoot, id);
        Directory.CreateDirectory(destination);
        try
        {
            var framePath = Path.Combine(destination, "idle.png");
            using (var stream = File.Create(framePath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }

            var manifest = new PetManifest
            {
                Id = id,
                Name = name.Trim(),
                Description = description.Trim(),
                CanvasSize = canvasSize,
                FrameDurationMs = 360,
                States = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["idle"] = ["idle.png"]
                }
            };
            WriteManifest(destination, manifest);
            var validation = Validate(destination);
            if (!validation.IsValid)
                throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));

            AppLog.Write($"Created pixel pet {id} on a {canvasSize}x{canvasSize} canvas");
            return new PetLibraryItem(id, manifest.Name, manifest.Description ?? string.Empty, destination);
        }
        catch
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, true);
            throw;
        }
    }

    public void UpdateMetadata(PetLibraryItem item, string name, string description)
    {
        EnsureOwned(item);
        var manifest = ReadManifest(Path.Combine(item.PackPath, "pet.json"));
        manifest.Name = string.IsNullOrWhiteSpace(name) ? item.Id : name.Trim();
        manifest.Description = description.Trim();
        WriteManifest(item.PackPath, manifest);
        AppLog.Write($"Updated pet metadata: {item.Id}");
    }

    public void Delete(PetLibraryItem item)
    {
        EnsureOwned(item);
        var fullPath = Path.GetFullPath(item.PackPath);
        var fullRoot = Path.GetFullPath(LibraryRoot) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只能删除本地宠物库中的宠物。");
        Directory.Delete(fullPath, true);
        AppLog.Write($"Deleted pet: {item.Id}");
    }

    public void Export(PetLibraryItem item, string destinationFile)
    {
        EnsureOwned(item);
        if (File.Exists(destinationFile))
            File.Delete(destinationFile);
        ZipFile.CreateFromDirectory(item.PackPath, destinationFile,
            CompressionLevel.Optimal, includeBaseDirectory: false);
        AppLog.Write($"Exported pet {item.Id} to {destinationFile}");
    }

    private static void EnsureOwned(PetLibraryItem item)
    {
        if (item.IsBuiltIn || string.IsNullOrWhiteSpace(item.PackPath))
            throw new InvalidOperationException("内置宠物不能修改、删除或导出。");
    }

    private static string ResolveManifestPath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidDataException("请选择宠物包目录或 pet.json。");
        var path = Path.GetFullPath(configuredPath);
        var manifestPath = Directory.Exists(path) ? Path.Combine(path, "pet.json") : path;
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("没有找到 pet.json。", manifestPath);
        if (!string.Equals(Path.GetFileName(manifestPath), "pet.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择 pet.json 或 .codexpet 文件。");
        return manifestPath;
    }

    private static PetManifest ReadManifest(string manifestPath) =>
        JsonSerializer.Deserialize<PetManifest>(File.ReadAllText(manifestPath), JsonOptions)
        ?? throw new InvalidDataException("pet.json 内容无效。");

    private static void WriteManifest(string root, PetManifest manifest) =>
        File.WriteAllText(Path.Combine(root, "pet.json"), JsonSerializer.Serialize(manifest, JsonOptions));

    private static void ValidateFrame(string root, string relative, ICollection<string> errors)
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(root, relative));
            var safeRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"帧路径不能离开宠物包目录：{relative}");
                return;
            }
            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"帧只支持 PNG：{relative}");
                return;
            }
            if (!File.Exists(path))
            {
                errors.Add($"缺少动画帧：{relative}");
                return;
            }

            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var bitmap = decoder.Frames[0];
            if (bitmap.PixelWidth is < 1 or > 64 || bitmap.PixelHeight is < 1 or > 64)
                errors.Add($"帧 {relative} 必须在 1×1 到 64×64 像素之间。");

            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            if (!Enumerable.Range(0, converted.PixelWidth * converted.PixelHeight)
                    .Any(index => pixels[index * 4 + 3] < 255))
                errors.Add($"帧 {relative} 没有透明背景像素。");
        }
        catch (Exception ex)
        {
            errors.Add($"无法读取帧 {relative}：{ex.Message}");
        }
    }

    private static void ExtractArchiveSafely(string archivePath, string destination)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        const long maximumUncompressedBytes = 20 * 1024 * 1024;
        const int maximumEntries = 500;
        if (archive.Entries.Count > maximumEntries ||
            archive.Entries.Sum(entry => entry.Length) > maximumUncompressedBytes)
            throw new InvalidDataException("宠物包过大（最多 500 个文件、解压后 20 MB）。");

        var safeRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("宠物包包含不安全的文件路径。");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    private static string FindPackRoot(string extractedRoot)
    {
        var manifests = Directory.EnumerateFiles(extractedRoot, "pet.json", SearchOption.AllDirectories)
            .Take(2).ToArray();
        return manifests.Length switch
        {
            0 => throw new InvalidDataException("分享包中没有 pet.json。"),
            > 1 => throw new InvalidDataException("分享包中只能包含一个宠物。"),
            _ => Path.GetDirectoryName(manifests[0])!
        };
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static string NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var chars = value.Trim().ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? character : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private sealed class PetManifest
    {
        public string? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int CanvasSize { get; set; } = 64;
        public int FrameDurationMs { get; set; } = 360;
        public Dictionary<string, string[]> States { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
