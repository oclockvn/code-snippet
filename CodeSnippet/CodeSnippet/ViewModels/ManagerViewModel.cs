using System.Collections.ObjectModel;
using System.IO;
using CodeSnippet.Models;
using CodeSnippet.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CodeSnippet.ViewModels;

public sealed partial class ManagerViewModel : ObservableObject
{
    private readonly PromptRepository _repository;

    public ObservableCollection<Prompt> Prompts { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private Prompt? _selectedPrompt;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _editTitle = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _editBody = string.Empty;

    [ObservableProperty]
    private string _editTags = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private PromptSortMode _sortMode = PromptSortMode.Recent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOnboarding))]
    [NotifyPropertyChangedFor(nameof(ShowEditor))]
    private bool _isEmpty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOnboarding))]
    [NotifyPropertyChangedFor(nameof(ShowEditor))]
    private bool _isComposing;

    /// <summary>Empty-library onboarding vs the normal two-pane editor (mocks G and F).</summary>
    public bool ShowOnboarding => IsEmpty && !IsComposing;

    public bool ShowEditor => !ShowOnboarding;

    public ManagerViewModel(PromptRepository repository)
    {
        _repository = repository;
        ApplyFilterAndSort();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilterAndSort();

    partial void OnSortModeChanged(PromptSortMode value) => ApplyFilterAndSort();

    private void ApplyFilterAndSort()
    {
        var previouslySelectedId = SelectedPrompt?.Id;

        IEnumerable<Prompt> source = _repository.Prompts;
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var filter = FilterText;
            source = source.Where(p =>
                p.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                p.Body.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (p.Tags?.Any(t => t.Contains(filter, StringComparison.OrdinalIgnoreCase)) ?? false));
        }

        var ordered = PromptRepository.OrderBy(source, SortMode);

        Prompts.Clear();
        foreach (var prompt in ordered)
        {
            Prompts.Add(prompt);
        }

        IsEmpty = _repository.Prompts.Count == 0;
        SelectedPrompt = previouslySelectedId is { } id
            ? Prompts.FirstOrDefault(p => p.Id == id)
            : null;
    }

    [RelayCommand]
    private void SetSortMode(string mode) => SortMode = Enum.Parse<PromptSortMode>(mode);

    partial void OnSelectedPromptChanged(Prompt? value)
    {
        EditTitle = value?.Title ?? string.Empty;
        EditBody = value?.Body ?? string.Empty;
        EditTags = value?.Tags is { Length: > 0 } tags ? string.Join(", ", tags) : string.Empty;

        if (value is not null)
        {
            IsComposing = true;
        }
    }

    [RelayCommand]
    private void New()
    {
        SelectedPrompt = null;
        EditTitle = string.Empty;
        EditBody = string.Empty;
        EditTags = string.Empty;
        StatusMessage = string.Empty;
        IsComposing = true;
    }

    /// <summary>Opens a blank new-prompt form with the title prefilled, for the popup's "no match" flow.</summary>
    public void StartNewPromptWithTitle(string title)
    {
        New();
        EditTitle = title;
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(EditTitle) && !string.IsNullOrWhiteSpace(EditBody);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var tags = EditTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (SelectedPrompt is null)
        {
            var prompt = new Prompt
            {
                Title = EditTitle.Trim(),
                Body = EditBody,
                Tags = tags.Length > 0 ? tags : null,
            };

            _repository.Add(prompt);
            ApplyFilterAndSort();
            SelectedPrompt = Prompts.FirstOrDefault(p => p.Id == prompt.Id);
        }
        else
        {
            SelectedPrompt.Title = EditTitle.Trim();
            SelectedPrompt.Body = EditBody;
            SelectedPrompt.Tags = tags.Length > 0 ? tags : null;
            _repository.Update(SelectedPrompt);
            ApplyFilterAndSort();
        }

        StatusMessage = $"Saved — {_repository.Prompts.Count} prompts on disk.";
    }

    private bool CanDelete() => SelectedPrompt is not null;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedPrompt is null)
        {
            return;
        }

        _repository.Delete(SelectedPrompt.Id);
        ApplyFilterAndSort();
        New();
        if (IsEmpty)
        {
            IsComposing = false;
        }

        StatusMessage = "Deleted.";
    }

    [RelayCommand]
    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = $"prompts-export-{DateTime.Now:yyyyMMdd-HHmmss}.json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, _repository.ExportToJson());
        StatusMessage = $"Exported {Prompts.Count} prompts.";
    }

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = "JSON files (*.json)|*.json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var imported = PromptRepository.ParseImport(json);
            if (imported is not { Count: > 0 })
            {
                StatusMessage = "No prompts found in file.";
                return;
            }

            var count = _repository.Import(imported);
            ApplyFilterAndSort();

            StatusMessage = $"Imported {count} prompts.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }
}
