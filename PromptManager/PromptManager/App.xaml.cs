using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using CodeSnippet.Models;
using CodeSnippet.Services;
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

        var searchViewModel = new SearchPopupViewModel(_repository, _pasteService, _settings.ShowPreviewPane);
        searchViewModel.CreatePromptRequested += OnCreatePromptRequested;
        searchViewModel.ImportRequested += OnImportRequested;
        _searchPopupWindow = new SearchPopupWindow(searchViewModel);

        // Force the HWND and one full layout/render pass now, so the hotkey path never pays for it.
        _searchPopupWindow.Show();
        _searchPopupWindow.Hide();

        _hotkeyService = new HotkeyService();
        RegisterConfiguredHotkey();
        _hotkeyService.HotkeyPressed += (_, _) => _searchPopupWindow.ShowForHotkey();

        var iconUri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
        _trayIconService = new TrayIconService(iconUri);
        _trayIconService.SearchRequested += (_, _) => _searchPopupWindow.ShowForHotkey();
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

    private ManagerWindow EnsureManagerWindow()
    {
        _managerWindow ??= new ManagerWindow(new ManagerViewModel(_repository));
        return _managerWindow;
    }

    private void ShowManagerWindow()
    {
        var window = EnsureManagerWindow();
        window.Show();
        window.Activate();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            var modifiers = (HotkeyService.Modifiers)_settings.HotkeyModifiers;
            var key = (Key)_settings.HotkeyKey;
            var dataFolderPath = Path.GetDirectoryName(_repository.FilePath) ?? string.Empty;
            var viewModel = new SettingsViewModel(
                _startupService,
                dataFolderPath,
                modifiers,
                key,
                _settings.ShowPreviewPane,
                ApplyNewHotkey,
                ApplyShowPreviewPane);
            _settingsWindow = new SettingsWindow(viewModel);
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplyShowPreviewPane(bool value)
    {
        _settings.ShowPreviewPane = value;
        _settingsStore.Save(_settings);
        _searchPopupWindow.ViewModel.ShowPreviewPane = value;
    }

    private void OnCreatePromptRequested(string title)
    {
        var window = EnsureManagerWindow();
        window.ViewModel.StartNewPromptWithTitle(title);
        window.Show();
        window.Activate();
    }

    private void OnImportRequested()
    {
        var window = EnsureManagerWindow();
        window.Show();
        window.Activate();
        window.ViewModel.ImportCommand.Execute(null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIconService?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
