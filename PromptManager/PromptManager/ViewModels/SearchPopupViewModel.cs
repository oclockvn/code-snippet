using System.Collections.ObjectModel;
using System.Windows.Threading;
using CodeSnippet.Models;
using CodeSnippet.Services;
using CodeSnippet.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PromptManager.ViewModels;

public sealed partial class SearchPopupViewModel : ObservableObject
{
    // Keeps the popup's visual tree small and render cost low, per the <100ms show budget.
    private const int MaxVisibleResults = 30;
    private const int RecentGroupSize = 5;

    private readonly PromptRepository _repository;
    private readonly PasteService _pasteService;
    private readonly List<Prompt> _searchBuffer = new();
    private readonly List<SearchResultRow> _selectable = new();
    private readonly List<(int Start, int Length)> _highlightRangeBuffer = new();
    private readonly DispatcherTimer _copiedTimer;
    private Prompt? _pendingHide;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private PopupState _state = PopupState.Resting;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private Prompt? _selectedPrompt;

    [ObservableProperty]
    private int _previewCharCount;

    [ObservableProperty]
    private string _copiedTitle = string.Empty;

    [ObservableProperty]
    private string _countBadgeText = string.Empty;

    [ObservableProperty]
    private bool _showPreviewPane = true;

    [ObservableProperty]
    private bool _isHeaderVisible = true;

    [ObservableProperty]
    private bool _isResultsVisible = true;

    [ObservableProperty]
    private bool _isQueryEmpty = true;

    [ObservableProperty]
    private bool _isPreviewVisible = true;

    public ObservableCollection<SearchResultRow> Rows { get; } = new();

    /// <summary>Fired once the 400ms "Copied" confirmation has held; the window should hide now.</summary>
    public event Action<Prompt>? PromptChosen;

    public event Action? Cancelled;

    /// <summary>Enter with no match (or on the first-run screen) — open the Manager with this title prefilled.</summary>
    public event Action<string>? CreatePromptRequested;

    /// <summary>'I' pressed on the first-run screen — open the Manager and trigger Import directly.</summary>
    public event Action? ImportRequested;

    public SearchPopupViewModel(PromptRepository repository, PasteService pasteService, bool showPreviewPane)
    {
        _repository = repository;
        _pasteService = pasteService;
        _showPreviewPane = showPreviewPane;

        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _copiedTimer.Tick += OnCopiedTimerTick;
    }

    public void Reset()
    {
        _copiedTimer.Stop();
        Query = string.Empty;
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
        IsPreviewVisible = IsResultsVisible && ShowPreviewPane;
    }

    partial void OnShowPreviewPaneChanged(bool value) => IsPreviewVisible = IsResultsVisible && value;

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

        SelectedPrompt = value >= 0 && value < _selectable.Count ? _selectable[value].Prompt : null;
        PreviewCharCount = SelectedPrompt?.Body.Length ?? 0;
    }

    private void RefreshResults()
    {
        Rows.Clear();
        _selectable.Clear();

        TotalCount = _repository.Prompts.Count;

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
            CountBadgeText = $"{TotalCount} prompt{(TotalCount == 1 ? "" : "s")}";
        }
        else
        {
            _repository.Search(Query, _searchBuffer);
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
                foreach (var prompt in _searchBuffer)
                {
                    AddItemRow(prompt, Query, includeTimestamp: false);
                }

                CountBadgeText = $"{_selectable.Count} of {TotalCount}";
            }
        }

        var newIndex = _selectable.Count > 0 ? 0 : -1;
        SelectedIndex = newIndex;
        SyncSelectionHighlight(newIndex);
    }

    private void BuildRestingRows()
    {
        var recent = _repository.Prompts
            .Where(p => p.LastUsedAt is not null)
            .OrderByDescending(p => p.LastUsedAt)
            .Take(RecentGroupSize)
            .ToList();

        var recentIds = new HashSet<Guid>(recent.Select(p => p.Id));

        var remainingSlots = Math.Max(0, MaxVisibleResults - recent.Count);
        var mostUsed = _repository.Prompts
            .Where(p => !recentIds.Contains(p.Id))
            .OrderByDescending(p => p.UsageCount)
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .Take(remainingSlots)
            .ToList();

        AppendGroup("Recent", recent);
        AppendGroup("Most used", mostUsed);
    }

    private void AppendGroup(string header, List<Prompt> prompts)
    {
        if (prompts.Count == 0)
        {
            return;
        }

        Rows.Add(new SearchResultRow { GroupHeader = header });
        foreach (var prompt in prompts)
        {
            AddItemRow(prompt, highlightQuery: null, includeTimestamp: true);
        }
    }

    private void AddItemRow(Prompt prompt, string? highlightQuery, bool includeTimestamp)
    {
        var index = _selectable.Count;

        var row = new SearchResultRow
        {
            GroupHeader = null,
            Prompt = prompt,
            SelectableIndex = index,
            DisplayNumber = index + 1,
            TitleSegments = BuildTitleSegments(prompt.Title, highlightQuery),
            MetaText = BuildMetaText(prompt, includeTimestamp),
            BodyPreview = ToSingleLine(prompt.Body),
        };

        Rows.Add(row);
        _selectable.Add(row);
    }

    private List<TitleSegment> BuildTitleSegments(string title, string? query)
    {
        if (string.IsNullOrEmpty(query) || !FuzzyMatcher.TryMatch(title, query, out _, _highlightRangeBuffer))
        {
            return new List<TitleSegment> { new(title, IsMatch: false) };
        }

        var segments = new List<TitleSegment>(_highlightRangeBuffer.Count * 2 + 1);
        var pos = 0;

        foreach (var (start, length) in _highlightRangeBuffer)
        {
            if (start > pos)
            {
                segments.Add(new TitleSegment(title[pos..start], IsMatch: false));
            }

            segments.Add(new TitleSegment(title.Substring(start, length), IsMatch: true));
            pos = start + length;
        }

        if (pos < title.Length)
        {
            segments.Add(new TitleSegment(title[pos..], IsMatch: false));
        }

        return segments;
    }

    private static string ToSingleLine(string body)
    {
        var start = 0;
        while (start < body.Length && char.IsWhiteSpace(body[start]))
        {
            start++;
        }

        var newline = body.IndexOfAny(NewlineChars, start);
        var firstLine = newline < 0 ? body[start..] : body[start..newline];
        return firstLine.Trim();
    }

    private static readonly char[] NewlineChars = { '\r', '\n' };

    private static string BuildMetaText(Prompt prompt, bool includeTimestamp)
    {
        if (includeTimestamp && prompt.LastUsedAt is { } lastUsed)
        {
            return $"{RelativeTimeFormatter.Format(lastUsed, sentenceCase: false)} · {prompt.UsageCount}×";
        }

        return $"{prompt.UsageCount}×";
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

    /// <summary>Alt+1..Alt+9 jump-select, per the popup's "Alt + N Jump" hint. 1-based, no-op out of range.</summary>
    [RelayCommand]
    private void Confirm()
    {
        switch (State)
        {
            case PopupState.NoMatch:
            case PopupState.FirstRun:
                CreatePromptRequested?.Invoke(Query.Trim());
                break;
            case PopupState.Copied:
                break;
            default:
                ChooseSelected();
                break;
        }
    }

    [RelayCommand]
    private void ChooseRow(SearchResultRow? row)
    {
        if (row is null || row.IsHeader)
        {
            return;
        }

        SelectedIndex = row.SelectableIndex;
    }

    private void ChooseSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= _selectable.Count)
        {
            return;
        }

        var prompt = _selectable[SelectedIndex].Prompt!;
        _ = _pasteService.CopyToClipboardAsync(prompt.Body);
        _repository.RecordUsage(prompt.Id);

        CopiedTitle = prompt.Title;
        State = PopupState.Copied;

        _pendingHide = prompt;
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    private void OnCopiedTimerTick(object? sender, EventArgs e)
    {
        _copiedTimer.Stop();
        if (_pendingHide is { } prompt)
        {
            _pendingHide = null;
            PromptChosen?.Invoke(prompt);
        }
    }

    /// <summary>'I' (or the "Import JSON" button) on the first-run screen — jump straight into Import.</summary>
    [RelayCommand]
    private void RequestImport() => ImportRequested?.Invoke();

    [RelayCommand]
    private void Cancel()
    {
        _copiedTimer.Stop();
        Cancelled?.Invoke();
    }
}
