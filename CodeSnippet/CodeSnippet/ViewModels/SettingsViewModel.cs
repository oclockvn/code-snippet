using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using CodeSnippet.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CodeSnippet.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly StartupService _startupService;
    private readonly Action<HotkeyService.Modifiers, Key> _applyHotkey;
    private readonly Action<string> _applyVaultPath;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private HotkeyService.Modifiers _hotkeyModifiers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private Key _hotkeyKey;

    [ObservableProperty]
    private string _vaultPath;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string HotkeyDisplay => FormatHotkey(HotkeyModifiers, HotkeyKey);

    public SettingsViewModel(
        StartupService startupService,
        HotkeyService.Modifiers currentModifiers,
        Key currentKey,
        string vaultPath,
        Action<HotkeyService.Modifiers, Key> applyHotkey,
        Action<string> applyVaultPath)
    {
        _startupService = startupService;
        _applyHotkey = applyHotkey;
        _applyVaultPath = applyVaultPath;
        _startWithWindows = startupService.IsEnabled();
        _hotkeyModifiers = currentModifiers;
        _hotkeyKey = currentKey;
        _vaultPath = vaultPath;
    }

    [RelayCommand]
    private void BrowseVaultFolder()
    {
        var dialog = new OpenFolderDialog();
        if (!string.IsNullOrEmpty(VaultPath) && Directory.Exists(VaultPath))
        {
            dialog.InitialDirectory = VaultPath;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        VaultPath = dialog.FolderName;
        _applyVaultPath(VaultPath);
        StatusMessage = "Vault folder updated.";
    }

    // Guards against the configured vault folder having been moved or deleted since it was set.
    [RelayCommand]
    private void OpenVaultFolder()
    {
        if (string.IsNullOrEmpty(VaultPath) || !Directory.Exists(VaultPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(VaultPath) { UseShellExecute = true });
    }

    partial void OnStartWithWindowsChanged(bool value) => _startupService.SetEnabled(value);

    public void SetCapturedHotkey(HotkeyService.Modifiers modifiers, Key key)
    {
        HotkeyModifiers = modifiers;
        HotkeyKey = key;
    }

    [RelayCommand]
    private void ApplyHotkey()
    {
        _applyHotkey(HotkeyModifiers, HotkeyKey);
        StatusMessage = $"Hotkey set to {HotkeyDisplay}.";
    }

    public static string FormatHotkey(HotkeyService.Modifiers modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotkeyService.Modifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyService.Modifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyService.Modifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyService.Modifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}
