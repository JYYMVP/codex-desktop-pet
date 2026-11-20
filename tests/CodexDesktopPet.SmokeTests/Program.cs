using System.Windows;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexDesktopPet;
using CodexDesktopPet.Models;
using CodexDesktopPet.Services;

var failures = new List<string>();
var desktop = new Rect(0, 0, 1920, 1080);
var visiblePetAndDock = new Rect(133, 304, 104, 142);

Check("top edge keeps the pet visible", 100, -304,
    WindowPlacement.ClampToDesktop(100, -900, visiblePetAndDock, desktop));
Check("bottom edge keeps the dock visible", 100, 634,
    WindowPlacement.ClampToDesktop(100, 900, visiblePetAndDock, desktop));
Check("left edge keeps controls visible", -133, 100,
    WindowPlacement.ClampToDesktop(-900, 100, visiblePetAndDock, desktop));
Check("right edge keeps controls visible", 1683, 100,
    WindowPlacement.ClampToDesktop(1900, 100, visiblePetAndDock, desktop));
Check("an in-bounds position is unchanged", 654, 321,
    WindowPlacement.ClampToDesktop(654, 321, visiblePetAndDock, desktop));

var twoMonitors = new Rect(-1920, -180, 3840, 1260);
Check("negative multi-monitor coordinates remain usable", -2053, -484,
    WindowPlacement.ClampToDesktop(-4000, -4000, visiblePetAndDock, twoMonitors));

CheckActivity("no chat activity is idle", CodexActivityState.Idle, []);
CheckActivity("ready chat is visible", CodexActivityState.Ready, [CodexActivityState.Ready]);
CheckActivity("blocked outranks ready", CodexActivityState.Blocked,
    [CodexActivityState.Ready, CodexActivityState.Blocked]);
CheckActivity("running outranks blocked", CodexActivityState.Running,
    [CodexActivityState.Blocked, CodexActivityState.Running]);
CheckActivity("input request outranks running", CodexActivityState.NeedsInput,
    [CodexActivityState.Running, CodexActivityState.NeedsInput]);
CheckActivitySnapshotOrdering();
CheckCodexDesktopPathValidation();
CheckCodexLifetimeDecision();
CheckActivityDescriptions();
CheckPetInteractionMenu();
CheckHookLifecycleContract();
CheckShowPetActionContract();
CheckPetEnableControlContract();
CheckComposerLayout();
CheckAnimatedPetPackage();
CheckGistShareLinkValidation();
CheckShareArchiveIntegrity();

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} smoke test(s) failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("23 placement, lifecycle, activity, interaction, plugin-control, editor, package, and share-security smoke tests passed.");
return 0;

void Check(string name, double expectedLeft, double expectedTop, Point actual)
{
    const double tolerance = 0.001;
    if (Math.Abs(actual.X - expectedLeft) > tolerance ||
        Math.Abs(actual.Y - expectedTop) > tolerance)
    {
        failures.Add(
            $"{name}: expected Left={expectedLeft}, Top={expectedTop}; " +
            $"actual Left={actual.X}, Top={actual.Y}");
    }
}

void CheckActivity(
    string name,
    CodexActivityState expected,
    IEnumerable<CodexActivityState> states)
{
    var actual = CodexActivityMonitor.Aggregate(states);
    if (actual != expected)
    {
        failures.Add($"{name}: expected {expected}; actual {actual}");
    }
}

void CheckComposerLayout()
{
    var document = XDocument.Load(
        System.IO.Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
    var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
    var composer = document.Descendants().SingleOrDefault(
        element => (string?)element.Attribute(xNamespace + "Name") == "ComposerPopup");

    if (composer is null ||
        composer.Name.LocalName != "Popup" ||
        (string?)composer.Attribute("Placement") != "Top" ||
        !((string?)composer.Attribute("PlacementTarget") ?? string.Empty).Contains("PetBody") ||
        !double.TryParse((string?)composer.Attribute("HorizontalOffset"), out var offset) ||
        offset >= 0)
    {
        failures.Add(
            "quick input must be an independent popup above PetBody so it cannot cover the pet");
    }

    var voiceItem = document.Descendants().SingleOrDefault(element =>
        element.Name.LocalName == "MenuItem" &&
        ((string?)element.Attribute("Header") ?? string.Empty).Contains("语音输入"));
    if (voiceItem is null || string.IsNullOrWhiteSpace((string?)voiceItem.Attribute("Click")))
        failures.Add("pet menu must expose a wired Windows voice-typing entry");
}

void CheckPetInteractionMenu()
{
    var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
    var actionTags = document.Descendants()
        .Where(element => element.Name.LocalName == "MenuItem" &&
                          (string?)element.Attribute("Click") == "PlayPetAction_Click")
        .Select(element => (string?)element.Attribute("Tag"))
        .Where(tag => tag is not null)
        .ToHashSet(StringComparer.Ordinal);
    var expected = new[] { "Waving", "Jumping", "RunningLeft", "RunningRight" };
    if (!expected.All(actionTags.Contains))
        failures.Add("pet context menu must expose wired wave, jump, and directional run actions");
}

void CheckHookLifecycleContract()
{
    using var hooks = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "hooks.json")));
    var root = hooks.RootElement.GetProperty("hooks");
    var requiredEvents = new[]
    {
        "SessionStart", "UserPromptSubmit", "PreModelRequest", "PreToolUse", "PostToolUse",
        "SubagentStart", "SubagentStop", "PermissionRequest", "Stop", "Interrupt", "SessionEnd"
    };
    if (!requiredEvents.All(name => root.TryGetProperty(name, out _)))
    {
        failures.Add("plugin hooks must cover startup and detailed Codex lifecycle events");
        return;
    }

    var commands = root.EnumerateObject()
        .SelectMany(entry => entry.Value.EnumerateArray())
        .SelectMany(group => group.GetProperty("hooks").EnumerateArray())
        .Select(hook => hook.GetProperty("command").GetString() ?? string.Empty)
        .ToArray();
    if (commands.Any(command => !command.Contains("-NonInteractive", StringComparison.Ordinal) ||
                                !command.Contains("-WindowStyle Hidden", StringComparison.Ordinal)) ||
        !root.GetProperty("UserPromptSubmit").EnumerateArray()
            .SelectMany(group => group.GetProperty("hooks").EnumerateArray())
            .Any(hook => (hook.GetProperty("command").GetString() ?? string.Empty)
                .Contains("start-pet.ps1", StringComparison.Ordinal)))
        failures.Add("plugin hooks must restart the pet on the next prompt without a console window");
}

void CheckShowPetActionContract()
{
    var server = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings-server.ps1"));
    if (!server.Contains("tool = \"pet.show\"", StringComparison.Ordinal) ||
        !server.Contains("name = \"pet.show\"", StringComparison.Ordinal) ||
        !server.Contains("function Show-InstalledPet", StringComparison.Ordinal) ||
        !server.Contains("PetHidden = $false", StringComparison.Ordinal))
        failures.Add("plugin details must expose a Show Pet action that clears hidden state and starts the helper");
}

void CheckPetEnableControlContract()
{
    var server = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings-server.ps1"));
    var startup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "start-pet.ps1"));
    using var mcp = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "mcp.json")));
    var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));

    if (!server.Contains("PetEnabled = $true", StringComparison.Ordinal) ||
        !server.Contains("property = \"PetEnabled\"", StringComparison.Ordinal) ||
        !startup.Contains("PetEnabled", StringComparison.Ordinal) ||
        !startup.Contains("startup skipped", StringComparison.Ordinal) ||
        !mcp.RootElement.GetProperty("mcpServers").TryGetProperty("pet-controls", out _) ||
        !xaml.Contains("彻底停用桌宠（从插件页重新启用）", StringComparison.Ordinal))
        failures.Add("the plugin must expose a real enable switch and keep hooks from reviving an explicitly disabled pet");
}

void CheckActivitySnapshotOrdering()
{
    var now = DateTime.UtcNow;
    var ordered = CodexActivityMonitor.OrderSnapshots(
    [
        new CodexActivitySnapshot("ready", CodexActivityState.Ready, "Stop", now),
        new CodexActivitySnapshot("running", CodexActivityState.Running, "PreModelRequest", now),
        new CodexActivitySnapshot("input", CodexActivityState.NeedsInput, "PermissionRequest", now),
        new CodexActivitySnapshot("blocked", CodexActivityState.Blocked, "Interrupt", now)
    ]);
    var expected = new[] { "input", "running", "blocked", "ready" };
    if (!ordered.Select(snapshot => snapshot.SessionHash).SequenceEqual(expected))
        failures.Add("multi-chat activity snapshots are not ordered by actionable priority");
}

void CheckCodexDesktopPathValidation()
{
    var trusted = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1111.9664.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe";
    var wrongPackage = @"C:\Program Files\WindowsApps\OpenAI.ChatGPT_1.0.0_x64__example\app\ChatGPT.exe";
    var cli = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1111.9664.0_x64__2p2nqsd0c76g0\app\resources\codex.exe";
    if (!CodexDesktopLifetimeMonitor.IsTrustedCodexDesktopExecutablePath(trusted) ||
        CodexDesktopLifetimeMonitor.IsTrustedCodexDesktopExecutablePath(wrongPackage) ||
        CodexDesktopLifetimeMonitor.IsTrustedCodexDesktopExecutablePath(cli))
        failures.Add("lifetime binding must recognize only the trusted Codex desktop executable");
}

void CheckCodexLifetimeDecision()
{
    var misses = 0;
    if (CodexDesktopLifetimeMonitor.ShouldExitAfterObservation(misses, false, out misses) ||
        CodexDesktopLifetimeMonitor.ShouldExitAfterObservation(misses, false, out misses) ||
        !CodexDesktopLifetimeMonitor.ShouldExitAfterObservation(misses, false, out misses))
    {
        failures.Add("lifetime binding must debounce three missing-host observations");
        return;
    }
    CodexDesktopLifetimeMonitor.ShouldExitAfterObservation(misses, true, out misses);
    if (misses != 0)
        failures.Add("lifetime binding must reset its debounce when Codex returns");
}

void CheckActivityDescriptions()
{
    if (MainWindow.DescribeActivity(CodexActivityState.Running, "PreModelRequest") != "正在思考" ||
        MainWindow.DescribeActivity(CodexActivityState.Running, "PreToolUse") != "正在执行工具" ||
        MainWindow.DescribeActivity(CodexActivityState.NeedsInput, "PermissionRequest") != "等待你确认" ||
        MainWindow.DescribeActivity(CodexActivityState.Ready, "Stop") != "任务已完成")
        failures.Add("lifecycle hook events must map to useful privacy-preserving status labels");
}

void CheckAnimatedPetPackage()
{
    var temporaryRoot = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "CodexDesktopPet.SmokeTests", Guid.NewGuid().ToString("N"));
    try
    {
        var sourceLibrary = new PetLibraryService(
            System.IO.Path.Combine(temporaryRoot, "source-library"));
        var frames = new Dictionary<PetState, IReadOnlyList<BitmapSource>>
        {
            [PetState.Idle] =
            [
                CreateTestFrame(8, Colors.DeepSkyBlue, 2, 2),
                CreateTestFrame(8, Colors.Cyan, 3, 2)
            ],
            [PetState.Thinking] = [CreateTestFrame(8, Colors.MediumPurple, 4, 3)]
        };
        var created = sourceLibrary.CreatePixelPet(
            "Animated smoke pet", "timeline test", 8, 125, frames);
        var validation = sourceLibrary.Validate(created.PackPath);
        if (!validation.IsValid)
            throw new InvalidDataException(string.Join("; ", validation.Errors));

        using (var manifest = JsonDocument.Parse(System.IO.File.ReadAllText(
                   System.IO.Path.Combine(created.PackPath, "pet.json"))))
        {
            var root = manifest.RootElement;
            if (root.GetProperty("sourceFormat").GetString() != "codex-pixel-pet-v2" ||
                root.GetProperty("frameDurationMs").GetInt32() != 125 ||
                root.GetProperty("states").GetProperty("idle").GetArrayLength() != 2 ||
                root.GetProperty("states").GetProperty("thinking").GetArrayLength() != 1)
                throw new InvalidDataException("animated manifest content mismatch");
        }

        var packagePath = System.IO.Path.Combine(temporaryRoot, "animated.codexpet");
        sourceLibrary.Export(created, packagePath);
        var targetLibrary = new PetLibraryService(
            System.IO.Path.Combine(temporaryRoot, "target-library"));
        var imported = targetLibrary.Import(packagePath);
        if (!targetLibrary.Validate(imported.PackPath).IsValid)
            throw new InvalidDataException("re-imported animated package is invalid");
    }
    catch (Exception ex)
    {
        failures.Add($"animated pet create/export/import: {ex.Message}");
    }
    finally
    {
        var fullTemporaryRoot = System.IO.Path.GetFullPath(temporaryRoot);
        var fullSystemTemporaryRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        if (fullTemporaryRoot.StartsWith(fullSystemTemporaryRoot,
                StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullTemporaryRoot))
            Directory.Delete(fullTemporaryRoot, recursive: true);
    }
}

BitmapSource CreateTestFrame(int size, Color color, int x, int y)
{
    var bytes = new byte[size * size * 4];
    var index = (y * size + x) * 4;
    bytes[index] = color.B;
    bytes[index + 1] = color.G;
    bytes[index + 2] = color.R;
    bytes[index + 3] = byte.MaxValue;
    var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
    bitmap.WritePixels(new Int32Rect(0, 0, size, size), bytes, size * 4, 0);
    bitmap.Freeze();
    return bitmap;
}

void CheckGistShareLinkValidation()
{
    const string id = "0123456789abcdef0123456789abcdef";
    if (!GitHubGistPetShareService.TryParseGistId(
            $"https://gist.github.com/JYYMVP/{id}", out var parsed) || parsed != id ||
        !GitHubGistPetShareService.TryParseGistId(id.ToUpperInvariant(), out parsed) ||
        parsed != id ||
        GitHubGistPetShareService.TryParseGistId(
            $"http://gist.github.com/JYYMVP/{id}", out _) ||
        GitHubGistPetShareService.TryParseGistId(
            $"https://example.com/JYYMVP/{id}", out _))
        failures.Add("public pet sharing must accept only HTTPS GitHub Gist links or valid IDs");
}

void CheckShareArchiveIntegrity()
{
    var archive = Enumerable.Range(0, 2048).Select(index => (byte)(index % 251)).ToArray();
    var base64 = Convert.ToBase64String(archive, Base64FormattingOptions.InsertLineBreaks);
    var hash = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
    var decoded = GitHubGistPetShareService.DecodeArchive(base64, hash);
    var rejectedCorruption = false;
    try { GitHubGistPetShareService.DecodeArchive(base64, new string('0', 64)); }
    catch (InvalidDataException) { rejectedCorruption = true; }
    if (!decoded.SequenceEqual(archive) || !rejectedCorruption)
        failures.Add("public pet share decoding must verify SHA-256 integrity");
}
