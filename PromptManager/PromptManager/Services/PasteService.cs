using System.Runtime.InteropServices;
using System.Windows;

namespace PromptManager.Services;

/// <summary>
/// Restores focus to whatever window was active before the hotkey fired, then pastes text into it
/// via the clipboard + a simulated Ctrl+V. This chain is dominated by OS/clipboard-broker latency,
/// not app code.
/// </summary>
public sealed class PasteService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const int InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public static IntPtr CaptureForegroundWindow() => GetForegroundWindow();

    /// <returns>
    /// False if the target window couldn't be foregrounded or the clipboard couldn't be written
    /// (e.g. the target is an elevated process and UIPI blocked us) -- callers should fall back to
    /// telling the user the text is on the clipboard for a manual paste.
    /// </returns>
    public async Task<bool> PasteIntoAsync(IntPtr targetWindow, string text)
    {
        if (targetWindow == IntPtr.Zero || string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (!SetForegroundWindow(targetWindow))
        {
            return false;
        }

        // Give the target window a brief moment to actually take focus before touching the clipboard.
        await Task.Delay(30);

        var previousClipboard = TryGetClipboardText();

        if (!TrySetClipboardText(text))
        {
            return false;
        }

        var sequenceAfterOurPaste = GetClipboardSequenceNumber();
        SendCtrlV();

        _ = RestoreClipboardLaterAsync(previousClipboard, sequenceAfterOurPaste);
        return true;
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            KeyDown(VkControl),
            KeyDown(VkV),
            KeyUp(VkV),
            KeyUp(VkControl),
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyDown(ushort vk) => new() { type = InputKeyboard, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } };

    private static INPUT KeyUp(ushort vk) => new()
    {
        type = InputKeyboard,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KeyEventFKeyUp } },
    };

    // Clipboard.SetText/GetText can throw COMException if another process has the clipboard open
    // momentarily -- a known flaky Win32 API, so retry a couple of times with a short backoff.
    private static string? TryGetClipboardText()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                Thread.Sleep(15);
            }
        }

        return null;
    }

    private static bool TrySetClipboardText(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(15);
            }
        }

        return false;
    }

    private async Task RestoreClipboardLaterAsync(string? previousClipboard, uint sequenceAfterOurPaste)
    {
        await Task.Delay(1000);

        // If the sequence number moved past what our own paste produced, the user (or another app)
        // put something new on the clipboard in the meantime -- leave it alone.
        if (GetClipboardSequenceNumber() != sequenceAfterOurPaste || previousClipboard is null)
        {
            return;
        }

        TrySetClipboardText(previousClipboard);
    }
}
