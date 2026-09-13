namespace PromptManager.Models;

public class AppSettings
{
    /// <summary>Raw Win32 RegisterHotKey modifier flags (MOD_ALT|MOD_CONTROL|MOD_SHIFT|MOD_WIN).</summary>
    public uint HotkeyModifiers { get; set; } = 0x0002 | 0x0001; // Control + Alt

    /// <summary>The <see cref="System.Windows.Input.Key"/> enum value, stored as its underlying int.</summary>
    public int HotkeyKey { get; set; } = 80; // Key.P
}
