using System.Runtime.InteropServices;

namespace CodexDesktopPet.Services;

internal static class VoiceTypingService
{
    private const byte VirtualKeyLeftWindows = 0x5B;
    private const byte VirtualKeyH = 0x48;
    private const uint KeyEventKeyUp = 0x0002;

    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10);

    public static void Start()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Windows 语音输入需要 Windows 10 或更高版本。");

        keybd_event(VirtualKeyLeftWindows, 0, 0, UIntPtr.Zero);
        keybd_event(VirtualKeyH, 0, 0, UIntPtr.Zero);
        keybd_event(VirtualKeyH, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VirtualKeyLeftWindows, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        UIntPtr extraInfo);
}
