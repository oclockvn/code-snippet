using System.Diagnostics;
using System.Windows.Input;
using CodeSnippet.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeSnippet.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly StartupService _startupService;
    private readonly string _dataFolderPath;
    private readonly Action<HotkeyService.Modifiers, Key> _applyHotkey;
    private readonly Action<bool> _applyShowPreviewPane;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _showPreviewPane;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private HotkeyService.Modifiers _hotkeyModifiers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private Key _hotkeyKey;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string HotkeyDisplay => FormatHotkey(HotkeyModifiers, HotkeyKey);

    public SettingsViewModel(
        StartupService startupService,
        string dataFolderPath,
        HotkeyService.Modifiers currentModifiers,
        Key currentKey,
        bool showPreviewPane,
        Action<HotkeyService.Modifiers, Key> applyHotkey,
        Action<bool> applyShowPreviewPane)
    {
        _startupService = startupService;
        _dataFolderPath = dataFolderPath;
        _applyHotkey = applyHotkey;
        _applyShowPreviewPane = applyShowPreviewPane;
        _startWithWindows = startupService.IsEnabled();
        _hotkeyModifiers = currentModifiers;
        _hotkeyKey = currentKey;
        _showPreviewPane = showPreviewPane;
    }

    [RelayCommand]
    private void ShowDataFolder() => Process.Start(new ProcessStartInfo(_dataFolderPath) { UseShellExecute = true });

    partial void OnStartWithWindowsChanged(bool value) => _startupService.SetEnabled(value);

    partial void OnShowPreviewPaneChanged(bool value) => _applyShowPreviewPane(value);

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
