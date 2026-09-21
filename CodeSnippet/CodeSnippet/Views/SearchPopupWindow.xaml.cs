using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CodeSnippet.ViewModels;

namespace CodeSnippet.Views;

public partial class SearchPopupWindow : Window
{
    private readonly SearchPopupViewModel _viewModel;

    public SearchPopupViewModel ViewModel => _viewModel;

    public SearchPopupWindow(SearchPopupViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.Cancelled += HideAndReset;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    // ItemsControl (unlike ListBox) doesn't auto-scroll its selection into view, so drive it manually.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SearchPopupViewModel.SelectedIndex))
        {
            return;
        }

        var row = _viewModel.Rows.FirstOrDefault(r => r.SelectableIndex == _viewModel.SelectedIndex);
        if (row is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (ResultsList.ItemContainerGenerator.ContainerFromItem(row) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        });
    }

    // State (query text, results, preview) is already clean by the time this runs — HideAndReset
    // reset it on the way out last time, off the hotkey-press critical path. So showing again is just
    // window plumbing: no Reindex/RefreshResults cost paid here.
    public void ShowForHotkey()
    {
        PositionOnActiveScreen();
        Visibility = Visibility.Visible;
        Activate();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void PositionOnActiveScreen()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + ((workArea.Width - Width) / 2);
        Top = workArea.Top + ((workArea.Height - Height) / 3);
    }

    // Clears the query/results/preview and re-reindexes the vault here, right before hiding, rather
    // than on the next ShowForHotkey — nobody's watching the popup while it's hidden, so the
    // Reindex/RefreshResults cost lands off the hotkey-press latency path instead of on it. Every
    // path that hides this window (Cancel, deactivate, opening Settings from the popup) must go
    // through this rather than a raw Hide(), or the next ShowForHotkey will show stale state.
    public void HideAndReset()
    {
        _viewModel.Reset();
        Visibility = Visibility.Hidden;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                // Progressive: first Esc collapses an open preview back to the plain result list;
                // only a second Esc (preview already closed) dismisses the whole popup.
                if (_viewModel.IsPreviewOpen)
                {
                    _viewModel.ClosePreviewCommand.Execute(null);
                }
                else
                {
                    _viewModel.CancelCommand.Execute(null);
                }

                e.Handled = true;
                break;
            case Key.Enter when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                _viewModel.CopyWithoutFencesCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Control:
                _viewModel.CopySelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Shift:
                _viewModel.CopyFilePathCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                _viewModel.ConfirmCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                _viewModel.MoveSelectionDownCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.MoveSelectionUpCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (Visibility == Visibility.Visible)
        {
            HideAndReset();
        }
    }
}
