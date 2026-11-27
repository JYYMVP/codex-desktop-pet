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
CheckExternalWindowActionsContract();
CheckPetEnableControlContract();
CheckPetStatusContract();
CheckSandboxDataRootContract();
CheckSettingsCorruptionContract();
CheckAtomicUpdaterContract();
CheckExitSemantics();
CheckVisualLauncherContract();
CheckWindowsIconContract();
CheckComposerLayout();
CheckAnimatedPetPackage();
CheckGistShareLinkValidation();
CheckShareArchiveIntegrity();
CheckCorruptSettingsRecovery();

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} smoke test(s) failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("Placement, lifecycle, activity, interaction, launcher, plugin-control, icon, editor, package, and share-security smoke tests passed.");
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

void CheckExternalWindowActionsContract()
{
    var app = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "App.xaml.cs.txt"));
    var mainWindow = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "MainWindow.xaml.cs.txt"));
    var server = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings-server.ps1"));
    if (!app.Contains("SettingsEventName", StringComparison.Ordinal) ||
        !app.Contains("LibraryEventName", StringComparison.Ordinal) ||
        !app.Contains("--settings", StringComparison.Ordinal) ||
        !app.Contains("--library", StringComparison.Ordinal) ||
        !mainWindow.Contains("OpenSettingsFromExternalRequest", StringComparison.Ordinal) ||
        !mainWindow.Contains("OpenPetLibraryFromExternalRequest", StringComparison.Ordinal) ||
        !server.Contains("name = \"pet.open-settings\"", StringComparison.Ordinal) ||
        !server.Contains("name = \"pet.open-library\"", StringComparison.Ordinal))
        failures.Add("advanced settings and pet library must be directly reachable outside the transparent pet window");
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
        !xaml.Contains("停用并退出…", StringComparison.Ordinal))
        failures.Add("the plugin must expose a real enable switch and keep hooks from reviving an explicitly disabled pet");
}

void CheckPetStatusContract()
{
    var server = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings-server.ps1"));
    if (!server.Contains("tool = \"pet.status\"", StringComparison.Ordinal) ||
        !server.Contains("name = \"pet.status\"", StringComparison.Ordinal) ||
        !server.Contains("function Get-PetStatusReport", StringComparison.Ordinal) ||
        !server.Contains("$pluginVersion = \"0.10.8\"", StringComparison.Ordinal) ||
        !server.Contains("Installed version", StringComparison.Ordinal) ||
        !server.Contains("unexpectedProcessPaths", StringComparison.Ordinal) ||
        !server.Contains("installedVersionMatches", StringComparison.Ordinal) ||
        !server.Contains("StatusNextVersionMismatch", StringComparison.Ordinal))
        failures.Add("plugin details must expose a read-only status check for installed version, path, and process state");
}

void CheckSandboxDataRootContract()
{
    var sandboxPath =
        @"C:\Users\Example\AppData\Local\Packages\openai.codex_2p2nqsd0c76g0\AC\#!sandbox.{00000000-0000-0000-0000-000000000000}";
    if (!AppPaths.IsCodexSandboxPath(sandboxPath) ||
        AppPaths.IsCodexSandboxPath(@"C:\Users\Example\AppData\Local"))
    {
        failures.Add("Codex sandbox LocalApplicationData paths must be distinguished from the stable user path");
    }

    var previousEnabled = Environment.GetEnvironmentVariable(
        "CODEX_DESKTOP_PET_ENABLE_TEST_ROOT");
    var previousRoot = Environment.GetEnvironmentVariable("CODEX_DESKTOP_PET_DATA_ROOT");
    var testRoot = Path.Combine(Path.GetTempPath(), "CodexDesktopPet-path-contract");
    try
    {
        Environment.SetEnvironmentVariable("CODEX_DESKTOP_PET_ENABLE_TEST_ROOT", "1");
        Environment.SetEnvironmentVariable("CODEX_DESKTOP_PET_DATA_ROOT", testRoot);
        if (!string.Equals(AppPaths.DataRoot, Path.GetFullPath(testRoot),
                StringComparison.OrdinalIgnoreCase))
            failures.Add("desktop helper data root must support an explicitly gated test override");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_DESKTOP_PET_ENABLE_TEST_ROOT", previousEnabled);
        Environment.SetEnvironmentVariable("CODEX_DESKTOP_PET_DATA_ROOT", previousRoot);
    }

    var sharedPaths = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "shared-paths.ps1"));
    if (!sharedPaths.Contains("#!sandbox", StringComparison.Ordinal) ||
        !sharedPaths.Contains("SpecialFolder]::UserProfile", StringComparison.Ordinal))
        failures.Add("plugin scripts must resolve Codex sandbox paths to the stable user-local data root");
}

void CheckSettingsCorruptionContract()
{
    var server = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings-server.ps1"));
    if (!server.Contains("settings file is invalid and was not modified", StringComparison.Ordinal) ||
        server.Contains("catch { $raw = [pscustomobject]@{} }", StringComparison.Ordinal))
        failures.Add("plugin settings updates must reject corrupt JSON instead of overwriting it with defaults");
}

void CheckCorruptSettingsRecovery()
{
    var temporaryRoot = Path.Combine(
        Path.GetTempPath(), "CodexDesktopPet.SettingsRecovery", Guid.NewGuid().ToString("N"));
    var previousEnabled = Environment.GetEnvironmentVariable(
        "CODEX_DESKTOP_PET_ENABLE_TEST_ROOT");
    var previousRoot = Environment.GetEnvironmentVariable("CODEX_DESKTOP_PET_DATA_ROOT");
    try
    {
        Directory.CreateDirectory(temporaryRoot);
        Environment.SetEnvironmentVariable("CODEX_DESKTOP_PET_ENABLE_TEST_ROOT", "1");
        Environment.SetEnvironmentVariable("CODEX_DESKTOP_PET_DATA_ROOT", temporaryRoot);
        File.WriteAllText(Path.Combine(temporaryRoot, "settings.json"), "{broken json");

        _ = AppSettings.Load();
        using var recovered = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(temporaryRoot, "settings.json")));
        if (!recovered.RootElement.TryGetProperty("PetEnabled", out _) ||
            Directory.EnumerateFiles(temporaryRoot, "settings.json.corrupt-*").Count() != 1)
            failures.Add("invalid desktop settings must be backed up before defaults are restored");
    }
    catch (Exception ex)
    {
        failures.Add($"settings recovery threw: {ex.Message}");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_DESKTOP_PET_ENABLE_TEST_ROOT", previousEnabled);
        Environment.SetEnvironmentVariable("CODEX_DESKTOP_PET_DATA_ROOT", previousRoot);
        try { Directory.Delete(temporaryRoot, true); } catch { }
    }
}

void CheckAtomicUpdaterContract()
{
    var updater = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "install-latest-release.ps1"));
    var server = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings-server.ps1"));
    if (!updater.Contains("[string]$ExpectedVersion", StringComparison.Ordinal) ||
        !updater.Contains(".app.backup-", StringComparison.Ordinal) ||
        !updater.Contains("Move-Item -LiteralPath $replacementDirectory", StringComparison.Ordinal) ||
        !server.Contains("-ExpectedVersion $pluginVersion", StringComparison.Ordinal))
        failures.Add("plugin updates must pin the helper version and atomically switch application directories");
}

void CheckExitSemantics()
{
    var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
    var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs.txt"));
    if (!xaml.Contains("Header=\"退出本次运行\" Click=\"ExitMenu_Click\"", StringComparison.Ordinal) ||
        !xaml.Contains("Header=\"停用并退出…\" Click=\"DisableAndExitMenu_Click\"", StringComparison.Ordinal) ||
        !source.Contains("MessageBoxResult.No", StringComparison.Ordinal) ||
        !source.Contains("if (result != MessageBoxResult.Yes)", StringComparison.Ordinal))
        failures.Add("one-run exit and persistent disable must be separate, with confirmation defaulting to cancel");
}

void CheckVisualLauncherContract()
{
    var app = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "App.xaml.cs.txt"));
    var installer = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "install-pet.ps1"));
    var rootInstaller = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "root-install.ps1"));
    var setup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CodexDesktopPet.iss"));
    if (!app.Contains("--show", StringComparison.Ordinal) ||
        !app.Contains("ShowEventName", StringComparison.Ordinal) ||
        !app.Contains("ShowFromExternalRequestAsync", StringComparison.Ordinal) ||
        !installer.Contains("CreateShortcut", StringComparison.Ordinal) ||
        !installer.Contains("$shortcut.Arguments = \"--show\"", StringComparison.Ordinal) ||
        !rootInstaller.Contains("$shortcut.Arguments = '--show'", StringComparison.Ordinal) ||
        !rootInstaller.Contains(".app.backup-", StringComparison.Ordinal) ||
        !setup.Contains("Parameters: \"--show\"", StringComparison.Ordinal))
        failures.Add("all installers must create a visual --show launcher that revives or reveals the single running instance");
}

void CheckWindowsIconContract()
{
    var project = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppProject.xml"));
    var applicationIcon = project.Descendants("ApplicationIcon").SingleOrDefault()?.Value;
    var setup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CodexDesktopPet.iss"));
    var mainWindowSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs.txt"));
    var iconPath = Path.Combine(AppContext.BaseDirectory, "CodexDesktopPet.ico");

    if (applicationIcon != @"Assets\CodexDesktopPet.ico" ||
        !setup.Contains("SetupIconFile=", StringComparison.Ordinal) ||
        !setup.Contains("UninstallDisplayIcon={app}\\CodexDesktopPet.exe", StringComparison.Ordinal) ||
        mainWindowSource.Contains("Icon = System.Drawing.SystemIcons.Application", StringComparison.Ordinal) ||
        !mainWindowSource.Contains("LoadApplicationIcon", StringComparison.Ordinal) ||
        !File.Exists(iconPath))
    {
        failures.Add("Windows EXE, WPF windows, tray, installer, and uninstall entry must share the application icon");
        return;
    }

    using var stream = File.OpenRead(iconPath);
    using var reader = new BinaryReader(stream);
    if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1)
    {
        failures.Add("application icon is not a valid ICO container");
        return;
    }
    var count = reader.ReadUInt16();
    var sizes = new HashSet<int>();
    for (var index = 0; index < count; index++)
    {
        var width = reader.ReadByte();
        var height = reader.ReadByte();
        sizes.Add(width == 0 ? 256 : width);
        reader.BaseStream.Position += 14;
    }
    if (!new[] { 16, 32, 48, 256 }.All(sizes.Contains))
        failures.Add("application icon must include 16, 32, 48, and 256 pixel frames");
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
