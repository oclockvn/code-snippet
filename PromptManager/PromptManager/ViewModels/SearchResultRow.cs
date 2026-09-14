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

    public string TitleBefore { get; init; } = string.Empty;

    public string TitleMatch { get; init; } = string.Empty;

    public string TitleAfter { get; init; } = string.Empty;

    public string MetaText { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
