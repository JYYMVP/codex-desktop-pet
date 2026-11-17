using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace CodexDesktopPet.Services;

internal sealed class GlobalHotKeyManager : IDisposable
{
    private const int HotKeyId = 0xC0DE;
    private const int WmHotKey = 0x0312;
    private readonly HwndSource _source;

    public event EventHandler? Pressed;

    public GlobalHotKeyManager(Window window, ModifierKeys modifiers, Key key)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException("无法获取桌宠窗口句柄。");
        _source.AddHook(WndProc);
        if (!RegisterHotKey(handle, HotKeyId, (uint)modifiers,
                (uint)KeyInterop.VirtualKeyFromKey(key)))
            throw new InvalidOperationException("快捷键 Ctrl+Alt+Q 已被其他程序占用。");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
        ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_source.Handle, HotKeyId);
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
