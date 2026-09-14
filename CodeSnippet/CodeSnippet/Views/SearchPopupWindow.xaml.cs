using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CodeSnippet.Models;
using CodeSnippet.ViewModels;
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

        _viewModel.PromptChosen += OnPromptChosen;
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

    private void OnPromptChosen(Prompt prompt) => HideBack();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.IsEditing)
        {
            // Let Enter/Up/Down behave normally inside the edit textboxes (newline, caret movement);
            // only Escape (cancel) and Ctrl+S (save) are intercepted globally.
            if (e.Key == Key.Escape)
            {
                _viewModel.CancelEditCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && _viewModel.SaveEditCommand.CanExecute(null))
            {
                _viewModel.SaveEditCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                _viewModel.CancelCommand.Execute(null);
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
            case Key.I when _viewModel.State == PopupState.FirstRun:
                _viewModel.RequestImportCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (Visibility == Visibility.Visible && !_viewModel.IsModalOpen)
        {
            HideBack();
        }
    }
}
