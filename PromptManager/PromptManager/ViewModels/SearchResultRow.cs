using CommunityToolkit.Mvvm.ComponentModel;
using PromptManager.Models;

namespace PromptManager.ViewModels;

/// <summary>
/// One visual row in the popup's result list: either a group header ("Recent" / "Most used") or a
/// prompt row with its title pre-split around the matched substring for highlighting.
/// </summary>
public sealed partial class SearchResultRow : ObservableObject
{
    public required string? GroupHeader { get; init; }

    public bool IsHeader => Prompt is null;

    public Prompt? Prompt { get; init; }

    /// <summary>Index into the flat selectable list (headers excluded); -1 for header rows.</summary>
    public int SelectableIndex { get; init; } = -1;

    public int DisplayNumber { get; init; }

    /// <summary>Title split into plain/matched runs for highlighting; matched runs may be non-contiguous (fuzzy match).</summary>
    public IReadOnlyList<TitleSegment> TitleSegments { get; init; } = Array.Empty<TitleSegment>();

    public string MetaText { get; init; } = string.Empty;

    /// <summary>Body collapsed to one line (embedded newlines flattened) for the result-row preview.</summary>
    public string BodyPreview { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One run of a row's title: either plain text or a fuzzy-match highlight.</summary>
public readonly record struct TitleSegment(string Text, bool IsMatch);
