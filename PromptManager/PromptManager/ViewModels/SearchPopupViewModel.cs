using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptManager.Models;
using PromptManager.Services;

namespace PromptManager.ViewModels;

public sealed partial class SearchPopupViewModel : ObservableObject
{
    // Keeps the popup's visual tree small and render cost low, per the <100ms show budget.
    private const int MaxVisibleResults = 30;

    private readonly PromptRepository _repository;
    private readonly List<Prompt> _searchBuffer = new();

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private int _selectedIndex;

    public ObservableCollection<Prompt> Results { get; } = new();

    public event Action<Prompt>? PromptChosen;

    public event Action? Cancelled;

    public SearchPopupViewModel(PromptRepository repository)
    {
        _repository = repository;
    }

    public void Reset()
    {
        Query = string.Empty;
        RefreshResults();
        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    partial void OnQueryChanged(string value)
    {
        RefreshResults();
        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    private void RefreshResults()
    {
        _repository.Search(Query, _searchBuffer);
        if (_searchBuffer.Count > MaxVisibleResults)
        {
            _searchBuffer.RemoveRange(MaxVisibleResults, _searchBuffer.Count - MaxVisibleResults);
        }

        SyncResults(_searchBuffer);
    }

    // In-place replace + trim against the ObservableCollection instead of Clear()+refill, so the
    // common case (same count, similar order) doesn't churn the ListBox's item containers.
    private void SyncResults(List<Prompt> latest)
    {
        for (var i = 0; i < latest.Count; i++)
        {
            if (i < Results.Count)
            {
                if (!ReferenceEquals(Results[i], latest[i]))
                {
                    Results[i] = latest[i];
                }
            }
            else
            {
                Results.Add(latest[i]);
            }
        }

        while (Results.Count > latest.Count)
        {
            Results.RemoveAt(Results.Count - 1);
        }
    }

    [RelayCommand]
    private void MoveSelectionDown()
    {
        if (Results.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Min(SelectedIndex + 1, Results.Count - 1);
    }

    [RelayCommand]
    private void MoveSelectionUp()
    {
        if (Results.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Max(SelectedIndex - 1, 0);
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
        {
            return;
        }

        PromptChosen?.Invoke(Results[SelectedIndex]);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
