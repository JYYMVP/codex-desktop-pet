using System.Windows;

namespace CodexDesktopPet.Services;

internal static class WindowPlacement
{
    public static System.Windows.Point ClampToDesktop(
        double requestedLeft,
        double requestedTop,
        Rect contentBounds,
        Rect desktopBounds)
    {
        var minimumLeft = desktopBounds.Left - contentBounds.Left;
        var maximumLeft = desktopBounds.Right - contentBounds.Right;
        var minimumTop = desktopBounds.Top - contentBounds.Top;
        var maximumTop = desktopBounds.Bottom - contentBounds.Bottom;

        return new System.Windows.Point(
            Math.Clamp(requestedLeft, minimumLeft, maximumLeft),
            Math.Clamp(requestedTop, minimumTop, maximumTop));
    }
}
