using System.Windows.Input;

namespace CodexDesktopPet.Models;

internal readonly record struct HotKeyGesture(ModifierKeys Modifiers, Key Key)
{
    public const string DefaultValue = "Ctrl+Alt+Q";

    public string DisplayName
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(FormatKey(Key));
            return string.Join("+", parts);
        }
    }

    public static HotKeyGesture ParseOrDefault(string? value) =>
        TryParse(value, out var gesture, out _) ? gesture :
            new HotKeyGesture(ModifierKeys.Control | ModifierKeys.Alt, Key.Q);

    public static bool TryCreate(ModifierKeys modifiers, Key key,
        out HotKeyGesture gesture, out string error)
    {
        gesture = default;
        modifiers &= ModifierKeys.Control | ModifierKeys.Alt |
                     ModifierKeys.Shift | ModifierKeys.Windows;

        if (modifiers == ModifierKeys.None)
        {
            error = "快捷键至少需要 Ctrl、Alt、Shift 或 Win 中的一个修饰键。";
            return false;
        }

        if (IsModifierKey(key) || key is Key.None or Key.System ||
            KeyInterop.VirtualKeyFromKey(key) == 0)
        {
            error = "请再按一个普通按键，例如 Q、Space 或 F8。";
            return false;
        }

        gesture = new HotKeyGesture(modifiers, key);
        error = string.Empty;
        return true;
    }

    public static bool TryParse(string? value, out HotKeyGesture gesture,
        out string error)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "快捷键不能为空。";
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries |
                                      StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "快捷键需要包含修饰键和普通按键。";
            return false;
        }

        var modifiers = ModifierKeys.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var modifier = parts[index].ToLowerInvariant();
            modifiers |= modifier switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };

            if (modifier is not ("ctrl" or "control" or "alt" or "shift" or
                "win" or "windows"))
            {
                error = $"无法识别修饰键：{parts[index]}。";
                return false;
            }
        }

        if (!TryParseKey(parts[^1], out var key))
        {
            error = $"无法识别按键：{parts[^1]}。请在输入框中直接按下组合键。";
            return false;
        }

        return TryCreate(modifiers, key, out gesture, out error);
    }

    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static bool TryParseKey(string value, out Key key)
    {
        if (value.Length == 1 && char.IsDigit(value[0]))
        {
            key = (Key)((int)Key.D0 + (value[0] - '0'));
            return true;
        }

        try
        {
            var converted = new KeyConverter().ConvertFromInvariantString(value);
            if (converted is Key parsed)
            {
                key = parsed;
                return true;
            }
        }
        catch { }

        key = Key.None;
        return false;
    }

    private static string FormatKey(Key key) => key is >= Key.D0 and <= Key.D9
        ? ((int)key - (int)Key.D0).ToString()
        : key.ToString();
}
