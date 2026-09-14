using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PromptManager.Models;
using PromptManager.Services;

namespace PromptManager.ViewModels;

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

    public ManagerViewModel(PromptRepository repository)
    {
        _repository = repository;
        foreach (var prompt in repository.Prompts)
        {
            Prompts.Add(prompt);
        }
    }

    partial void OnSelectedPromptChanged(Prompt? value)
    {
        EditTitle = value?.Title ?? string.Empty;
        EditBody = value?.Body ?? string.Empty;
        EditTags = value?.Tags is { Length: > 0 } tags ? string.Join(", ", tags) : string.Empty;
    }

    [RelayCommand]
    private void New()
    {
        SelectedPrompt = null;
        EditTitle = string.Empty;
        EditBody = string.Empty;
        EditTags = string.Empty;
        StatusMessage = string.Empty;
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
            Prompts.Add(prompt);
            SelectedPrompt = prompt;
        }
        else
        {
            SelectedPrompt.Title = EditTitle.Trim();
            SelectedPrompt.Body = EditBody;
            SelectedPrompt.Tags = tags.Length > 0 ? tags : null;
            _repository.Update(SelectedPrompt);
        }

        StatusMessage = "Saved.";
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
        Prompts.Remove(SelectedPrompt);
        New();
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

            Prompts.Clear();
            foreach (var prompt in _repository.Prompts)
            {
                Prompts.Add(prompt);
            }

            StatusMessage = $"Imported {count} prompts.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }
}
