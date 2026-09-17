using CodeSnippet.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodeSnippet.ViewModels;

/// <summary>
/// One visual row in the popup's result list: a file row with its name pre-split around the
/// matched substring for highlighting.
/// </summary>
public sealed partial class SearchResultRow : ObservableObject
{
    public required VaultFile File { get; init; }

    public int SelectableIndex { get; init; }

    public int DisplayNumber { get; init; }

    /// <summary>Name split into plain/matched runs for highlighting; matched runs may be non-contiguous (fuzzy match).</summary>
    public IReadOnlyList<TitleSegment> TitleSegments { get; init; } = Array.Empty<TitleSegment>();

    /// <summary>Containing folder, relative to the vault root; empty for a file at the vault root.</summary>
    public string FolderPath { get; init; } = string.Empty;

    public bool HasFolderPath => FolderPath.Length > 0;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One run of a row's title: either plain text or a fuzzy-match highlight.</summary>
public readonly record struct TitleSegment(string Text, bool IsMatch);
