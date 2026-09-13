using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptManager.Services;

namespace PromptManager.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly StartupService _startupService;
    private readonly Action<HotkeyService.Modifiers, Key> _applyHotkey;

    [ObservableProperty]
    private bool _startWithWindows;

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
        HotkeyService.Modifiers currentModifiers,
        Key currentKey,
        Action<HotkeyService.Modifiers, Key> applyHotkey)
    {
        _startupService = startupService;
        _applyHotkey = applyHotkey;
        _startWithWindows = startupService.IsEnabled();
        _hotkeyModifiers = currentModifiers;
        _hotkeyKey = currentKey;
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
