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

        _viewModel.Cancelled += HideBack;
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

    public void ShowForHotkey()
    {
        _viewModel.Reset();

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

    private void HideBack() => Visibility = Visibility.Hidden;

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
            HideBack();
        }
    }
}
