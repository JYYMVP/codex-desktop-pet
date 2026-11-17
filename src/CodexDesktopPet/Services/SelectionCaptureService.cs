using System.Windows;
using System.Windows.Automation;
using Forms = System.Windows.Forms;

namespace CodexDesktopPet.Services;

internal sealed class SelectionCaptureService
{
    public async Task<string?> CaptureAsync()
    {
        var automationText = TryCaptureWithUiAutomation();
        if (!string.IsNullOrWhiteSpace(automationText))
            return automationText.Trim();

        System.Windows.IDataObject? previousClipboard = null;
        try
        {
            previousClipboard = System.Windows.Clipboard.GetDataObject();
            System.Windows.Clipboard.Clear();
            Forms.SendKeys.SendWait("^c");
            await Task.Delay(180);
            return System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText().Trim()
                : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (previousClipboard is not null)
            {
                try { System.Windows.Clipboard.SetDataObject(previousClipboard, true); }
                catch { }
            }
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
