using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
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
    private readonly DispatcherTimer _toastTimer;
    private CancellationTokenSource? _previewCts;

    // How long a highlighted row has to stay highlighted before its preview is actually read from
    // disk. Arrow-key navigation moves the highlight faster than this, so passing-through rows never
    // trigger a read — only the row the user settles on does.
    private static readonly TimeSpan PreviewDebounceDelay = TimeSpan.FromMilliseconds(80);

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
    private bool _isToastVisible;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

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

    public event Action? Cancelled;

    /// <summary>Raised by the first-run screen's "Open Settings" button (or Enter, while in that state) — the app owns opening the Settings window.</summary>
    public event Action? SettingsRequested;

    public SearchPopupViewModel(VaultIndexService vaultIndex, PasteService pasteService)
    {
        _vaultIndex = vaultIndex;
        _pasteService = pasteService;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _toastTimer.Tick += OnToastTimerTick;
    }

    public void Reset()
    {
        _toastTimer.Stop();
        IsToastVisible = false;
        Query = string.Empty;
        StatusMessage = string.Empty;
        IsPreviewOpen = false;
        PreviewContent = string.Empty;
        _previewCts?.Cancel();
        // Force RefreshResults' closing "SelectedIndex = 0" below to be a genuine change even when
        // the popup was already left on row 0 — the generated property setter no-ops an unchanged
        // value, which would silently skip the PropertyChanged that SearchPopupWindow listens for to
        // scroll the list back into view. Without this, reopening after scrolling down (without ever
        // changing the selection) would show item 0 selected but the list still scrolled down.
        SelectedIndex = -1;
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
            RequestPreviewLoad(SelectedFile);
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

    // No LINQ / no sort here: VaultIndexService already keeps Files sorted most-recently-modified
    // first. Capped like the typed-search path — RefreshResults runs on every keystroke, including
    // the backspace that empties the query, not just once per popup open, so an uncapped rebuild here
    // paid a real WPF layout/virtualization cost on every one of those.
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
            // Opening the preview is a deliberate, one-shot action (Enter/click) — nothing to debounce
            // against yet, so load immediately. Only subsequent highlight changes while it's already
            // open (arrow-key navigation) go through the debounced path below.
            RequestPreviewLoad(SelectedFile, immediate: true);
        }
        else
        {
            _previewCts?.Cancel();
            PreviewContent = string.Empty;
        }
    }

    // Cancels whatever preview load is in flight and starts a fresh one. Arrow-key navigation calls
    // this on every row it passes through; with immediate: false, only the row the debounce delay
    // expires on ever reaches the file read in LoadPreviewAsync below.
    private void RequestPreviewLoad(VaultFile? file, bool immediate = false)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        _ = LoadPreviewAsync(file, immediate, cts.Token);
    }

    private async Task LoadPreviewAsync(VaultFile? file, bool immediate, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            PreviewContent = string.Empty;
            return;
        }

        if (!immediate)
        {
            try
            {
                await Task.Delay(PreviewDebounceDelay, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                // Superseded by a later highlight change before the debounce elapsed; no file read happened.
                return;
            }
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(file.FullPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            content = $"Couldn't read \"{file.Name}\" — it may have moved or been deleted.";
        }

        if (!cancellationToken.IsCancellationRequested)
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

        content = VaultContentExtractor.ExtractContent(content);
        await _pasteService.CopyToClipboardAsync(content);

        ShowToast($"Copied \"{file.Name}\"");
    }

    /// <summary>Ctrl+Shift+Enter: copy the current selection's full text, dropping every line that starts with ``` (fence open/close lines).</summary>
    [RelayCommand]
    private void CopyWithoutFences()
    {
        if (SelectedIndex < 0 || SelectedIndex >= _selectable.Count)
        {
            return;
        }

        _ = CopyWithoutFencesAsync(_selectable[SelectedIndex].File);
    }

    private async Task CopyWithoutFencesAsync(VaultFile file)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(file.FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Couldn't read \"{file.Name}\" — it may have moved or been deleted.";
            _vaultIndex.Reindex();
            RefreshResults();
            return;
        }

        var kept = content.Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal));
        await _pasteService.CopyToClipboardAsync(string.Join('\n', kept));

        ShowToast($"Copied \"{file.Name}\" (no fences)");
    }

    /// <summary>Shift+Enter: copy the current selection's absolute file path to the clipboard.</summary>
    [RelayCommand]
    private void CopyFilePath()
    {
        if (SelectedIndex < 0 || SelectedIndex >= _selectable.Count)
        {
            return;
        }

        var file = _selectable[SelectedIndex].File;
        _ = CopyFilePathAsync(file);
    }

    private async Task CopyFilePathAsync(VaultFile file)
    {
        await _pasteService.CopyToClipboardAsync(file.FullPath);

        ShowToast($"Path copied: \"{file.Name}\"");
    }

    /// <summary>Slides the top toast in and (re)starts the timer that slides it back out.</summary>
    private void ShowToast(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;

        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnToastTimerTick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        IsToastVisible = false;
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    [RelayCommand]
    private void Cancel()
    {
        _toastTimer.Stop();
        IsToastVisible = false;
        Cancelled?.Invoke();
    }
}
