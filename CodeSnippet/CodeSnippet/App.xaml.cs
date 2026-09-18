using System.Threading;
using System.Windows;
using System.Windows.Input;
using CodeSnippet.Models;
using CodeSnippet.Services;
using CodeSnippet.ViewModels;
using CodeSnippet.Views;

namespace CodeSnippet;

public partial class App : Application
{
    private const string MutexName = "CodeSnippet.SingleInstance.Mutex";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private VaultIndexService _vaultIndex = null!;
    private AppSettingsStore _settingsStore = null!;
    private AppSettings _settings = null!;
    private HotkeyService _hotkeyService = null!;
    private PasteService _pasteService = null!;
    private StartupService _startupService = null!;
    private TrayIconService _trayIconService = null!;

    private SearchPopupWindow _searchPopupWindow = null!;
    private SettingsWindow? _settingsWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _settingsStore = new AppSettingsStore();
        _settings = await _settingsStore.LoadAsync();

        _vaultIndex = new VaultIndexService();
        _vaultIndex.SetVaultPath(_settings.VaultPath);

        _pasteService = new PasteService();
        _startupService = new StartupService();

        // Tray icon goes up before the (expensive, ~400ms) popup window is built, so the user sees
        // the app has started immediately rather than waiting through XAML/JIT warm-up in silence.
        // Event handlers below close over _searchPopupWindow/_hotkeyService, which aren't assigned
        // yet — safe, since they're only read when an event actually fires, well after startup.
        var iconUri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
        _trayIconService = new TrayIconService(iconUri);
        _trayIconService.SearchRequested += (_, _) => _searchPopupWindow.ShowForHotkey();
        _trayIconService.SettingsRequested += (_, _) => ShowSettingsWindow();
        _trayIconService.ExitRequested += (_, _) => Shutdown();
        _trayIconService.Show();

        var searchViewModel = new SearchPopupViewModel(_vaultIndex, _pasteService);
        searchViewModel.SettingsRequested += OnSettingsRequestedFromPopup;
        _searchPopupWindow = new SearchPopupWindow(searchViewModel);

        // Force the HWND and one full layout/render pass now, so the hotkey path never pays for it.
        _searchPopupWindow.Show();
        _searchPopupWindow.Hide();

        // Also run the Reset -> Reindex -> RefreshResults -> Search path once now (hidden), so the
        // JIT/layout cost of the search machinery itself is paid here rather than on the user's
        // first real hotkey press.
        searchViewModel.Reset();

        _hotkeyService = new HotkeyService();
        RegisterConfiguredHotkey();
        _hotkeyService.HotkeyPressed += (_, _) => _searchPopupWindow.ShowForHotkey();
    }

    private void RegisterConfiguredHotkey()
    {
        var modifiers = (HotkeyService.Modifiers)_settings.HotkeyModifiers;
        var key = (Key)_settings.HotkeyKey;
        _hotkeyService.Register(_searchPopupWindow, modifiers, key);
    }

    private void ApplyNewHotkey(HotkeyService.Modifiers modifiers, Key key)
    {
        if (!_hotkeyService.Register(_searchPopupWindow, modifiers, key))
        {
            MessageBox.Show(
                "That key combination could not be registered (it may already be in use by another app).",
                "Prompt Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            RegisterConfiguredHotkey();
            return;
        }

        _settings.HotkeyModifiers = (uint)modifiers;
        _settings.HotkeyKey = (int)key;
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void OnSettingsRequestedFromPopup()
    {
        _searchPopupWindow.HideAndReset();
        ShowSettingsWindow();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            var modifiers = (HotkeyService.Modifiers)_settings.HotkeyModifiers;
            var key = (Key)_settings.HotkeyKey;
            var viewModel = new SettingsViewModel(
                _startupService,
                modifiers,
                key,
                _settings.VaultPath,
                ApplyNewHotkey,
                ApplyVaultPath);
            _settingsWindow = new SettingsWindow(viewModel);
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplyVaultPath(string vaultPath)
    {
        _settings.VaultPath = vaultPath;
        _ = _settingsStore.SaveAsync(_settings);
        _vaultIndex.SetVaultPath(vaultPath);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIconService?.Dispose();
        // A second launch shuts down without ever owning the mutex (createdNew was false) — releasing
        // it in that case throws, since the calling thread doesn't hold it.
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        base.OnExit(e);
    }
}
