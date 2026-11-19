using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace CodexDesktopPet.Services;

internal sealed class GlobalHotKeyManager : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private readonly HwndSource _source;
    private readonly int _hotKeyId;

    public event EventHandler? Pressed;

    public GlobalHotKeyManager(Window window, int hotKeyId, ModifierKeys modifiers,
        Key key, string displayName)
    {
        _hotKeyId = hotKeyId;
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException("无法获取桌宠窗口句柄。");
        _source.AddHook(WndProc);
        if (RegisterHotKey(handle, _hotKeyId, (uint)modifiers | ModNoRepeat,
                (uint)KeyInterop.VirtualKeyFromKey(key)))
            return;

        _source.RemoveHook(WndProc);
        throw new InvalidOperationException($"快捷键 {displayName} 已被其他程序占用。");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
        ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == _hotKeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_source.Handle, _hotKeyId);
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
