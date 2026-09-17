using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CodeSnippet.Models;
using CodeSnippet.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeSnippet.ViewModels;

public sealed partial class SearchPopupViewModel : ObservableObject
{
    // Keeps the popup's visual tree small and render cost low, per the <100ms show budget.
    private const int MaxVisibleResults = 30;

    private readonly VaultIndexService _vaultIndex;
    private readonly PasteService _pasteService;
    private readonly List<VaultFileMatch> _searchBuffer = new();
    private readonly List<SearchResultRow> _selectable = new();
    private readonly DispatcherTimer _copiedTimer;
    private VaultFile? _pendingHide;
    private int _previewRequestId;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private PopupState _state = PopupState.Resting;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private VaultFile? _selectedFile;

    [ObservableProperty]
    private string _copiedTitle = string.Empty;

    [ObservableProperty]
    private string _countBadgeText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isPreviewOpen;

    [ObservableProperty]
    private string _previewContent = string.Empty;

    [ObservableProperty]
    private bool _isHeaderVisible = true;

    [ObservableProperty]
    private bool _isResultsVisible = true;

    [ObservableProperty]
    private bool _isQueryEmpty = true;

    public ObservableCollection<SearchResultRow> Rows { get; } = new();

    /// <summary>Fired once the 400ms "Copied" confirmation has held; the window should hide now.</summary>
    public event Action<VaultFile>? FileChosen;

    public event Action? Cancelled;

    /// <summary>Raised by the first-run screen's "Open Settings" button (or Enter, while in that state) — the app owns opening the Settings window.</summary>
    public event Action? SettingsRequested;

    public SearchPopupViewModel(VaultIndexService vaultIndex, PasteService pasteService)
    {
        _vaultIndex = vaultIndex;
        _pasteService = pasteService;

        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _copiedTimer.Tick += OnCopiedTimerTick;
    }

    public void Reset()
    {
        _copiedTimer.Stop();
        Query = string.Empty;
        StatusMessage = string.Empty;
        IsPreviewOpen = false;
        PreviewContent = string.Empty;
        // Vault files change externally (Obsidian, sync, the user) while the popup is closed, so
        // pick up any changes now rather than trusting whatever was indexed at app startup.
        _vaultIndex.Reindex();
        RefreshResults();
    }

    partial void OnQueryChanged(string value)
    {
        IsQueryEmpty = string.IsNullOrEmpty(value);
        RefreshResults();
    }

    partial void OnStateChanged(PopupState value)
    {
        IsHeaderVisible = value is PopupState.Resting or PopupState.Typing or PopupState.NoMatch;
        IsResultsVisible = value is PopupState.Resting or PopupState.Typing;

        // Nothing sensible to preview once there's no valid selection underneath it (no results,
        // no vault configured) or the popup is already showing the copied-confirmation screen.
        if (!IsResultsVisible)
        {
            SetPreviewOpen(false);
        }
    }

    partial void OnSelectedIndexChanged(int value) => SyncSelectionHighlight(value);

    // Rows are rebuilt from scratch on every RefreshResults (fresh SearchResultRow instances, IsSelected
    // defaults to false), so the highlight must be re-applied unconditionally — it can't rely solely on
    // OnSelectedIndexChanged, which the generated property setter skips when the numeric index happens to
    // be unchanged from the previous open (e.g. reopening the popup with the same index-0 default).
    private void SyncSelectionHighlight(int value)
    {
        for (var i = 0; i < _selectable.Count; i++)
        {
            _selectable[i].IsSelected = _selectable[i].SelectableIndex == value;
        }

        SelectedFile = value >= 0 && value < _selectable.Count ? _selectable[value].File : null;

        // While the preview is open it tracks whatever row is highlighted — via arrow keys, a
        // click, or a new search result taking the top slot — not just the file it was opened for.
        if (IsPreviewOpen)
        {
            _ = LoadPreviewAsync(SelectedFile);
        }
    }

    private void RefreshResults()
    {
        Rows.Clear();
        _selectable.Clear();

        TotalCount = _vaultIndex.Files.Count;

        if (TotalCount == 0)
        {
            State = PopupState.FirstRun;
            SelectedIndex = -1;
            SyncSelectionHighlight(-1);
            return;
        }

        if (string.IsNullOrWhiteSpace(Query))
        {
            State = PopupState.Resting;
            BuildRestingRows();
            CountBadgeText = $"{TotalCount} file{(TotalCount == 1 ? "" : "s")}";
        }
        else
        {
            _vaultIndex.Search(Query, _searchBuffer);

            if (_searchBuffer.Count > MaxVisibleResults)
            {
                _searchBuffer.RemoveRange(MaxVisibleResults, _searchBuffer.Count - MaxVisibleResults);
            }

            if (_searchBuffer.Count == 0)
            {
                State = PopupState.NoMatch;
                CountBadgeText = $"0 of {TotalCount}";
            }
            else
            {
                State = PopupState.Typing;
                foreach (var match in _searchBuffer)
                {
                    AddItemRow(match.File, match.NameRanges);
                }

                CountBadgeText = $"{_selectable.Count} of {TotalCount}";
            }
        }

        var newIndex = _selectable.Count > 0 ? 0 : -1;
        SelectedIndex = newIndex;
        SyncSelectionHighlight(newIndex);
    }

    // No LINQ / no sort here: VaultIndexService already keeps Files sorted by name, so resting rows
    // are just the first page of it, capped to the visible budget.
    private void BuildRestingRows()
    {
        var files = _vaultIndex.Files;
        var count = Math.Min(files.Count, MaxVisibleResults);
        for (var i = 0; i < count; i++)
        {
            AddItemRow(files[i], nameRanges: null);
        }
    }

    private void AddItemRow(VaultFile file, IReadOnlyList<(int Start, int Length)>? nameRanges)
    {
        var index = _selectable.Count;

        var row = new SearchResultRow
        {
            File = file,
            SelectableIndex = index,
            DisplayNumber = index + 1,
            TitleSegments = BuildTitleSegments(file.Name, nameRanges),
            FolderPath = Path.GetDirectoryName(file.RelativePath) ?? string.Empty,
        };

        Rows.Add(row);
        _selectable.Add(row);
    }

    private static List<TitleSegment> BuildTitleSegments(string name, IReadOnlyList<(int Start, int Length)>? ranges)
    {
        if (ranges is null || ranges.Count == 0)
        {
            return new List<TitleSegment> { new(name, IsMatch: false) };
        }

        var segments = new List<TitleSegment>(ranges.Count * 2 + 1);
        var pos = 0;

        foreach (var (start, length) in ranges)
        {
            if (start > pos)
            {
                segments.Add(new TitleSegment(name[pos..start], IsMatch: false));
            }

            segments.Add(new TitleSegment(name.Substring(start, length), IsMatch: true));
            pos = start + length;
        }

        if (pos < name.Length)
        {
            segments.Add(new TitleSegment(name[pos..], IsMatch: false));
        }

        return segments;
    }

    [RelayCommand]
    private void MoveSelectionDown()
    {
        if (_selectable.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Min(SelectedIndex + 1, _selectable.Count - 1);
    }

    [RelayCommand]
    private void MoveSelectionUp()
    {
        if (_selectable.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Max(SelectedIndex - 1, 0);
    }

    /// <summary>Enter: open the inline preview for the current selection (first-run screen: jump to Settings instead).</summary>
    [RelayCommand]
    private void Confirm()
    {
        switch (State)
        {
            case PopupState.FirstRun:
                OpenSettings();
                break;
            case PopupState.NoMatch:
            case PopupState.Copied:
                break;
            default:
                SetPreviewOpen(true);
                break;
        }
    }

    /// <summary>Escape: collapse an open preview first; only closes the popup once the preview is already closed.</summary>
    [RelayCommand]
    private void ClosePreview() => SetPreviewOpen(false);

    private void SetPreviewOpen(bool open)
    {
        IsPreviewOpen = open;
        if (open)
        {
            _ = LoadPreviewAsync(SelectedFile);
        }
        else
        {
            PreviewContent = string.Empty;
        }
    }

    private async Task LoadPreviewAsync(VaultFile? file)
    {
        var requestId = ++_previewRequestId;

        if (file is null)
        {
            PreviewContent = string.Empty;
            return;
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(file.FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            content = $"Couldn't read \"{file.Name}\" — it may have moved or been deleted.";
        }

        // A later keystroke/arrow press may have moved the selection while this read was in
        // flight; only the most recently requested file gets to write PreviewContent.
        if (requestId == _previewRequestId)
        {
            PreviewContent = content;
        }
    }

    [RelayCommand]
    private void ChooseRow(SearchResultRow? row)
    {
        if (row is null)
        {
            return;
        }

        SelectedIndex = row.SelectableIndex;
    }

    /// <summary>Ctrl+Enter (or the preview's Copy button): copy the current selection's content to the clipboard directly, preview or no preview.</summary>
    [RelayCommand]
    private void CopySelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= _selectable.Count)
        {
            return;
        }

        _ = CopySelectedFileAsync(_selectable[SelectedIndex].File);
    }

    private async Task CopySelectedFileAsync(VaultFile file)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(file.FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file may have moved, been renamed, or been deleted since the index was built
            // (the vault is edited externally, e.g. by Obsidian, while this popup isn't watching).
            StatusMessage = $"Couldn't read \"{file.Name}\" — it may have moved or been deleted.";
            _vaultIndex.Reindex();
            RefreshResults();
            return;
        }

        await _pasteService.CopyToClipboardAsync(content);

        CopiedTitle = file.Name;
        State = PopupState.Copied;

        _pendingHide = file;
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    private void OnCopiedTimerTick(object? sender, EventArgs e)
    {
        _copiedTimer.Stop();
        if (_pendingHide is { } file)
        {
            _pendingHide = null;
            FileChosen?.Invoke(file);
        }
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    [RelayCommand]
    private void Cancel()
    {
        _copiedTimer.Stop();
        Cancelled?.Invoke();
    }
}
