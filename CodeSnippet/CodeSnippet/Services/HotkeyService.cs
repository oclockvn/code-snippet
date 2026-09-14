using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace CodeSnippet.Services;

/// <summary>
/// Wraps the Win32 RegisterHotKey API against a single persistent window handle (the pre-warmed
/// search popup), so the hotkey path never has to create a window or allocate.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4000;
    private const uint NoRepeat = 0x4000; // MOD_NOREPEAT: suppress repeats while the key is held down.

    [Flags]
    public enum Modifiers : uint
    {
        None = 0x0000,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public bool Register(Window messageWindow, Modifiers modifiers, Key key)
    {
        var handle = new WindowInteropHelper(messageWindow).EnsureHandle();

        if (_source is null)
        {
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WndProc);
        }

        if (_registered)
        {
            UnregisterHotKey(handle, HotkeyId);
            _registered = false;
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = RegisterHotKey(handle, HotkeyId, (uint)modifiers | NoRepeat, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_source is not null && _registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose() => Unregister();
}
