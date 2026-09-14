using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace PromptManager.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;

    public event EventHandler? SearchRequested;

    public event EventHandler? ManageRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public TrayIconService(Uri iconUri)
    {
        _taskbarIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(iconUri),
            ToolTipText = "Prompt Manager",
            ContextMenu = BuildContextMenu(),
        };

        _taskbarIcon.TrayMouseDoubleClick += (_, _) => SearchRequested?.Invoke(this, EventArgs.Empty);
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var search = new MenuItem { Header = "Search prompts" };
        search.Click += (_, _) => SearchRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(search);

        var manage = new MenuItem { Header = "Manage library" };
        manage.Click += (_, _) => ManageRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(manage);

        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exit);

        return menu;
    }

    public void Show() => _taskbarIcon.ForceCreate();

    public void Dispose() => _taskbarIcon.Dispose();
}
