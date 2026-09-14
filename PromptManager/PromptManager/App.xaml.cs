using System.Threading;
using System.Windows;
using System.Windows.Input;
using PromptManager.Models;
using PromptManager.Services;
using PromptManager.ViewModels;
using PromptManager.Views;

namespace PromptManager;

public partial class App : Application
{
    private const string MutexName = "PromptManager.SingleInstance.Mutex";

    private Mutex? _singleInstanceMutex;
    private PromptRepository _repository = null!;
    private AppSettingsStore _settingsStore = null!;
    private AppSettings _settings = null!;
    private HotkeyService _hotkeyService = null!;
    private PasteService _pasteService = null!;
    private StartupService _startupService = null!;
    private TrayIconService _trayIconService = null!;

    private SearchPopupWindow _searchPopupWindow = null!;
    private ManagerWindow? _managerWindow;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _repository = new PromptRepository();
        _repository.Load();

        _settingsStore = new AppSettingsStore();
        _settings = _settingsStore.Load();

        _pasteService = new PasteService();
        _startupService = new StartupService();

        var searchViewModel = new SearchPopupViewModel(_repository);
        _searchPopupWindow = new SearchPopupWindow(searchViewModel, _pasteService);

        // Force the HWND and one full layout/render pass now, so the hotkey path never pays for it.
        _searchPopupWindow.Show();
        _searchPopupWindow.Hide();

        _hotkeyService = new HotkeyService();
        RegisterConfiguredHotkey();
        _hotkeyService.HotkeyPressed += (_, _) =>
            _searchPopupWindow.ShowForHotkey(PasteService.CaptureForegroundWindow());

        var iconUri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
        _trayIconService = new TrayIconService(iconUri);
        _trayIconService.SearchRequested += (_, _) =>
            _searchPopupWindow.ShowForHotkey(PasteService.CaptureForegroundWindow());
        _trayIconService.ManageRequested += (_, _) => ShowManagerWindow();
        _trayIconService.SettingsRequested += (_, _) => ShowSettingsWindow();
        _trayIconService.ExitRequested += (_, _) => Shutdown();
        _trayIconService.Show();
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
        _settingsStore.Save(_settings);
    }

    private void ShowManagerWindow()
    {
        _managerWindow ??= new ManagerWindow(new ManagerViewModel(_repository));
        _managerWindow.Show();
        _managerWindow.Activate();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            var modifiers = (HotkeyService.Modifiers)_settings.HotkeyModifiers;
            var key = (Key)_settings.HotkeyKey;
            var viewModel = new SettingsViewModel(_startupService, modifiers, key, ApplyNewHotkey);
            _settingsWindow = new SettingsWindow(viewModel);
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIconService?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
