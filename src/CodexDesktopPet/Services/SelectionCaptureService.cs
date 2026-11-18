using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace CodexDesktopPet.Services;

internal sealed class SelectionCaptureService
{
    public async Task<string?> CaptureAsync()
    {
        await WaitForShortcutReleaseAsync();

        // Releasing Alt after a global shortcut can leave Windows applications in
        // menu-key-tip mode. Escape returns focus to the document without changing
        // the current text selection in common editors and browsers.
        Forms.SendKeys.SendWait("{ESC}");
        await Task.Delay(60);

        var automationText = TryCaptureWithUiAutomation();
        if (!string.IsNullOrWhiteSpace(automationText))
            return automationText.Trim();

        System.Windows.IDataObject? previousClipboard = null;
        try
        {
            previousClipboard = TryGetClipboardData();
            TryClearClipboard();
            Forms.SendKeys.SendWait("^c");
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(100);
                var text = TryGetClipboardText();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (previousClipboard is not null)
            {
                TryRestoreClipboard(previousClipboard);
            }
        }
    }

    private static System.Windows.IDataObject? TryGetClipboardData()
    {
        try { return System.Windows.Clipboard.GetDataObject(); }
        catch { return null; }
    }

    private static void TryClearClipboard()
    {
        try { System.Windows.Clipboard.Clear(); }
        catch { }
    }

    private static string? TryGetClipboardText()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryRestoreClipboard(System.Windows.IDataObject data)
    {
        try { System.Windows.Clipboard.SetDataObject(data, true); }
        catch { }
    }

    private static async Task WaitForShortcutReleaseAsync()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(900);
        while (DateTime.UtcNow < deadline &&
               (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt |
                                      ModifierKeys.Shift | ModifierKeys.Windows)) != ModifierKeys.None)
        {
            await Task.Delay(25);
        }
    }

    private static string? TryCaptureWithUiAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null || !focused.TryGetCurrentPattern(
                    TextPattern.Pattern, out var patternObject))
                return null;
            var ranges = ((TextPattern)patternObject).GetSelection();
            return ranges.Length > 0 ? ranges[0].GetText(-1) : null;
        }
        catch
        {
            return null;
        }
    }
}
