using CodeSnippet.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodeSnippet.ViewModels;

/// <summary>
/// One visual row in the popup's result list: a prompt row with its title pre-split around the
/// matched substring for highlighting.
/// </summary>
public sealed partial class SearchResultRow : ObservableObject
{
    public required Prompt Prompt { get; init; }

    public int SelectableIndex { get; init; }

    public int DisplayNumber { get; init; }

    /// <summary>Title split into plain/matched runs for highlighting; matched runs may be non-contiguous (fuzzy match).</summary>
    public IReadOnlyList<TitleSegment> TitleSegments { get; init; } = Array.Empty<TitleSegment>();

    /// <summary>Body collapsed to one line (embedded newlines flattened) for the result-row preview.</summary>
    public string BodyPreview { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One run of a row's title: either plain text or a fuzzy-match highlight.</summary>
public readonly record struct TitleSegment(string Text, bool IsMatch);
