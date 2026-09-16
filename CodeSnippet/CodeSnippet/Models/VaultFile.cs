namespace CodeSnippet.Models;

/// <summary>One indexed file under the configured vault folder.</summary>
public sealed class VaultFile
{
    public required string FullPath { get; init; }

    /// <summary>Path relative to the vault root, used to disambiguate same-named files and to derive the containing folder for display.</summary>
    public required string RelativePath { get; init; }

    /// <summary>File name without extension — the only thing search matches against.</summary>
    public required string Name { get; init; }
}
