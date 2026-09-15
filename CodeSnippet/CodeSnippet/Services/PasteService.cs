using System.Runtime.InteropServices;
using System.Windows;

namespace CodeSnippet.Services;

/// <summary>
/// Copies prompt text to the clipboard. The caller pastes manually (Ctrl+V) wherever they need it.
/// </summary>
public sealed class PasteService
{
    // Clipboard.SetText can throw COMException if another process has the clipboard open
    // momentarily -- a known flaky Win32 API, so retry a couple of times with a short backoff.
    // Clipboard access requires the UI (STA) thread, so the retry stays on the calling thread and
    // awaits Task.Delay instead of Thread.Sleep, so the backoff doesn't block the UI dispatcher.
    public async Task<bool> CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                await Task.Delay(15);
            }
        }

        return false;
    }
}
