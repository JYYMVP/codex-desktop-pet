using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexDesktopPet.Models;

namespace CodexDesktopPet.Services;

internal sealed class PetLibraryService
{
    private const int ChatGptCellWidth = 192;
    private const int ChatGptCellHeight = 208;
    private const int ChatGptColumns = 8;
    private const long MaximumChatGptSheetBytes = 20L * 1024 * 1024;
    private static readonly int[] ChatGptRequiredFrames = [6, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string LibraryRoot { get; }

    public PetLibraryService() : this(Path.Combine(AppPaths.DataRoot, "pets"))
    {
    }

    internal PetLibraryService(string libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
            throw new ArgumentException("宠物库路径不能为空。", nameof(libraryRoot));
        LibraryRoot = Path.GetFullPath(libraryRoot);
        Directory.CreateDirectory(LibraryRoot);
    }

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
            if (manifest.CanvasSize is < 1 or > ChatGptCellHeight)
                errors.Add($"canvasSize 必须在 1 到 {ChatGptCellHeight} 之间。");
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
                    ValidateFrame(root, relative, manifest.CanvasSize, errors);
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

    public PetLibraryItem ImportChatGptSpriteSheet(string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
            throw new FileNotFoundException("没有找到 ChatGPT 宠物精灵表。", source);
        var fileInfo = new FileInfo(source);
        if (fileInfo.Length is < 1 or > MaximumChatGptSheetBytes)
            throw new InvalidDataException("精灵表必须在 1 字节到 20 MiB 之间。");

        BitmapSource sheet;
        try
        {
            using var stream = File.OpenRead(source);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            sheet = decoder.Frames[0];
            sheet.Freeze();
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("无法读取精灵表；如果是 WebP，请先转换为透明 PNG。", ex);
        }

        var rowCount = sheet.PixelHeight switch
        {
            1872 => 9,
            2288 => 11,
            _ => 0
        };
        if (sheet.PixelWidth != ChatGptCellWidth * ChatGptColumns || rowCount == 0)
            throw new InvalidDataException(
                "ChatGPT 宠物精灵表必须是 1536×1872（v1）或 1536×2288（v2）。");

        var converted = new FormatConvertedBitmap(sheet, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        ValidateChatGptCells(converted, rowCount);

        var sourceName = Path.GetFileNameWithoutExtension(source).Trim();
        if (string.IsNullOrWhiteSpace(sourceName))
            sourceName = "ChatGPT Pet";
        var baseId = NormalizeId(sourceName);
        if (string.IsNullOrWhiteSpace(baseId))
            baseId = "chatgpt-pet";
        baseId = baseId[..Math.Min(baseId.Length, 28)];
        var id = $"{baseId}-{Guid.NewGuid():N}"[..(baseId.Length + 9)];
        var destination = Path.Combine(LibraryRoot, id);
        Directory.CreateDirectory(destination);

        try
        {
            var rowFiles = new Dictionary<int, string[]>();
            for (var row = 0; row < rowCount; row++)
            {
                var frameCount = ChatGptRequiredFrames[row];
                var files = new string[frameCount];
                for (var frame = 0; frame < frameCount; frame++)
                {
                    var relative = $"row-{row:D2}-frame-{frame:D2}.png";
                    var crop = new CroppedBitmap(converted,
                        new System.Windows.Int32Rect(frame * ChatGptCellWidth,
                            row * ChatGptCellHeight, ChatGptCellWidth, ChatGptCellHeight));
                    crop.Freeze();
                    using var output = File.Create(Path.Combine(destination, relative));
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(crop));
                    encoder.Save(output);
                    files[frame] = relative;
                }
                rowFiles[row] = files;
            }

            var states = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = rowFiles[0],
                ["runningRight"] = rowFiles[1],
                ["runningLeft"] = rowFiles[2],
                ["waving"] = rowFiles[3],
                ["jumping"] = rowFiles[4],
                ["failed"] = rowFiles[5],
                ["waiting"] = rowFiles[6],
                ["running"] = rowFiles[7],
                ["review"] = rowFiles[8],
                ["capturing"] = rowFiles[8],
                ["thinking"] = rowFiles[7],
                ["ready"] = rowFiles[8],
                ["needsInput"] = rowFiles[6],
                ["error"] = rowFiles[5]
            };
            if (rowCount == 11)
            {
                states["lookFirstHalf"] = rowFiles[9];
                states["lookSecondHalf"] = rowFiles[10];
            }

            var manifest = new PetManifest
            {
                Id = id,
                Name = sourceName,
                Description = rowCount == 11
                    ? "从 ChatGPT Pets v2 精灵表导入的本地宠物"
                    : "从 ChatGPT Pets v1 精灵表导入的本地宠物",
                CanvasSize = ChatGptCellHeight,
                FrameDurationMs = 150,
                PixelArt = false,
                SourceFormat = rowCount == 11 ? "chatgpt-pets-v2" : "chatgpt-pets-v1",
                States = states
            };
            WriteManifest(destination, manifest);
            var validation = Validate(destination);
            if (!validation.IsValid)
                throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));

            AppLog.Write($"Imported ChatGPT Pets sprite sheet. Id={id}; Rows={rowCount}; " +
                         $"Source={source}");
            return new PetLibraryItem(id, manifest.Name, manifest.Description ?? string.Empty,
                destination);
        }
        catch
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, true);
            throw;
        }
    }

    public PetLibraryItem CreateStaticPet(string name, string description, int canvasSize,
        BitmapSource bitmap) => CreatePixelPet(name, description, canvasSize, 360,
        new Dictionary<PetState, IReadOnlyList<BitmapSource>>
        {
            [PetState.Idle] = [bitmap]
        });

    public PetLibraryItem CreatePixelPet(
        string name,
        string description,
        int canvasSize,
        int frameDurationMs,
        IReadOnlyDictionary<PetState, IReadOnlyList<BitmapSource>> states)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("请给宠物起一个名字。");
        if (canvasSize is < 8 or > 64)
            throw new InvalidDataException("画布必须是 8×8 到 64×64 的正方形。");
        if (!states.TryGetValue(PetState.Idle, out var idleFrames) || idleFrames.Count == 0)
            throw new InvalidDataException("像素宠物至少需要一个待机帧。");
        if (states.Count > Enum.GetValues<PetState>().Length ||
            states.Any(pair => pair.Value.Count is < 1 or > 64))
            throw new InvalidDataException("每个动作需要 1 到 64 帧。");
        foreach (var frame in states.SelectMany(pair => pair.Value))
        {
            if (frame.PixelWidth != canvasSize || frame.PixelHeight != canvasSize)
                throw new InvalidDataException($"所有帧都必须是 {canvasSize}×{canvasSize} 像素。");
        }
        if (!idleFrames.Any(HasArtwork))
            throw new InvalidDataException("待机动画至少需要一个包含图像的帧。");

        var baseId = NormalizeId(name);
        if (string.IsNullOrWhiteSpace(baseId))
            baseId = "pixel-pet";
        baseId = baseId[..Math.Min(baseId.Length, 32)];
        var id = $"{baseId}-{Guid.NewGuid():N}"[..(baseId.Length + 9)];
        var destination = Path.Combine(LibraryRoot, id);
        Directory.CreateDirectory(destination);
        try
        {
            var manifestStates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (state, frames) in states.OrderBy(pair => pair.Key))
            {
                var stateName = ToManifestStateName(state);
                var frameFiles = new string[frames.Count];
                for (var index = 0; index < frames.Count; index++)
                {
                    var relative = $"{stateName}-{index + 1:D2}.png";
                    using var stream = File.Create(Path.Combine(destination, relative));
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(frames[index]));
                    encoder.Save(stream);
                    frameFiles[index] = relative;
                }
                manifestStates[stateName] = frameFiles;
            }

            var manifest = new PetManifest
            {
                Id = id,
                Name = name.Trim(),
                Description = description.Trim(),
                CanvasSize = canvasSize,
                FrameDurationMs = Math.Clamp(frameDurationMs, 80, 2000),
                PixelArt = true,
                SourceFormat = "codex-pixel-pet-v2",
                States = manifestStates
            };
            WriteManifest(destination, manifest);
            var validation = Validate(destination);
            if (!validation.IsValid)
                throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));

            AppLog.Write($"Created animated pixel pet {id}. Canvas={canvasSize}; " +
                         $"States={states.Count}; Frames={states.Sum(pair => pair.Value.Count)}");
            return new PetLibraryItem(id, manifest.Name, manifest.Description ?? string.Empty, destination);
        }
        catch
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, true);
            throw;
        }
    }

    private static bool HasArtwork(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return Enumerable.Range(0, converted.PixelWidth * converted.PixelHeight)
            .Any(index => pixels[index * 4 + 3] > 0);
    }

    private static string ToManifestStateName(PetState state)
    {
        var value = state.ToString();
        return char.ToLowerInvariant(value[0]) + value[1..];
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

    private static void ValidateFrame(string root, string relative, int maximumSize,
        ICollection<string> errors)
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
            if (bitmap.PixelWidth < 1 || bitmap.PixelWidth > maximumSize ||
                bitmap.PixelHeight < 1 || bitmap.PixelHeight > maximumSize)
                errors.Add($"帧 {relative} 必须在 1×1 到 {maximumSize}×{maximumSize} 像素之间。");

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

    private static void ValidateChatGptCells(BitmapSource bitmap, int rowCount)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var errors = new List<string>();

        for (var row = 0; row < rowCount; row++)
        for (var frame = 0; frame < ChatGptColumns; frame++)
        {
            var required = frame < ChatGptRequiredFrames[row];
            var hasArtwork = false;
            var hasTransparency = false;
            var startX = frame * ChatGptCellWidth;
            var startY = row * ChatGptCellHeight;
            for (var y = startY; y < startY + ChatGptCellHeight; y++)
            for (var x = startX; x < startX + ChatGptCellWidth; x++)
            {
                var alpha = pixels[y * stride + x * 4 + 3];
                hasArtwork |= alpha > 0;
                hasTransparency |= alpha < 255;
            }

            if (required && !hasArtwork)
                errors.Add($"第 {row + 1} 行第 {frame + 1} 帧缺少图像。");
            else if (required && !hasTransparency)
                errors.Add($"第 {row + 1} 行第 {frame + 1} 帧没有透明背景。");
            else if (!required && hasArtwork)
                errors.Add($"第 {row + 1} 行第 {frame + 1} 个未使用单元格必须完全透明。");
        }

        if (errors.Count > 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, errors.Take(12)) +
                (errors.Count > 12 ? $"{Environment.NewLine}另有 {errors.Count - 12} 项错误。" : string.Empty));
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
        public bool PixelArt { get; set; } = true;
        public string SourceFormat { get; set; } = "codex-pixel-pet-v1";
        public Dictionary<string, string[]> States { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
