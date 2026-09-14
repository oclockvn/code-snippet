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
    public Task<bool> CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(TrySetClipboardText(text));
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
}
