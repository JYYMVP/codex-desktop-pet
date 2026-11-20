using System.Windows;
using System.Xml.Linq;
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
CheckComposerLayout();

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} smoke test(s) failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("12 placement, activity, and quick-input layout smoke tests passed.");
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
}
