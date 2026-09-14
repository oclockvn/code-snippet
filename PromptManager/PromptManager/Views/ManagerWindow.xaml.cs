using System.ComponentModel;
using System.Windows;
using PromptManager.ViewModels;

namespace PromptManager.Views;

public partial class ManagerWindow : Window
{
    public ManagerViewModel ViewModel { get; }

    public ManagerWindow(ManagerViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    // Constructed once and reused: closing just hides it so reopening is instant.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
