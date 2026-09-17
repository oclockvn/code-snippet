namespace CodeSnippet.Models;

public class AppSettings
{
    /// <summary>Raw Win32 RegisterHotKey modifier flags (MOD_ALT|MOD_CONTROL|MOD_SHIFT|MOD_WIN).</summary>
    public uint HotkeyModifiers { get; set; } = 0x0001 | 0x0004; // Alt + Shift

    /// <summary>The <see cref="System.Windows.Input.Key"/> enum value, stored as its underlying int.</summary>
    public int HotkeyKey { get; set; } = 53; // Key.J

    /// <summary>Root folder to index for search (e.g. an Obsidian vault). Empty until configured in Settings.</summary>
    public string VaultPath { get; set; } = string.Empty;
}
