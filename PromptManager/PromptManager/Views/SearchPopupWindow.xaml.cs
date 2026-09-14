using System.Windows;
using System.Windows.Input;
using PromptManager.Models;
using PromptManager.Services;
using PromptManager.ViewModels;

namespace PromptManager.Views;

public partial class SearchPopupWindow : Window
{
    private readonly SearchPopupViewModel _viewModel;
    private readonly PasteService _pasteService;

    public SearchPopupWindow(SearchPopupViewModel viewModel, PasteService pasteService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _pasteService = pasteService;
        DataContext = _viewModel;

        _viewModel.PromptChosen += OnPromptChosen;
        _viewModel.Cancelled += HideBack;
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

    private async void OnPromptChosen(Prompt prompt)
    {
        HideBack();
        await _pasteService.CopyToClipboardAsync(prompt.Body);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
