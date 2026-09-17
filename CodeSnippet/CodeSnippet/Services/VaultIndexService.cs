using System.IO;
using CodeSnippet.Models;

namespace CodeSnippet.Services;

/// <summary>One search result: the matched file, plus its name's fuzzy-match highlight ranges
/// (null when there was no query).</summary>
public readonly record struct VaultFileMatch(VaultFile File, IReadOnlyList<(int Start, int Length)>? NameRanges);

/// <summary>
/// In-memory index of markdown files under a configured vault folder. The vault is owned and
/// mutated externally (Obsidian, git, sync tools, the user), so this class only ever reads — there
/// is no persistence here. Callers re-scan (<see cref="Reindex"/>) whenever the index might be
/// stale, e.g. on every popup open.
/// </summary>
public sealed class VaultIndexService
{
    private readonly List<VaultFile> _files = new();
    private readonly List<(VaultFile File, int Score, (int Start, int Length)[] Ranges)> _matchBuffer = new();
    private readonly List<(int Start, int Length)> _rangeScratch = new();
    private readonly FuzzyMatcher.Scratch _matchScratch = new();

    public string VaultPath { get; private set; } = string.Empty;

    public IReadOnlyList<VaultFile> Files => _files;

    /// <summary>Changes the indexed folder and immediately re-scans it.</summary>
    public void SetVaultPath(string vaultPath)
    {
        VaultPath = vaultPath;
        Reindex();
    }

    /// <summary>Re-scans <see cref="VaultPath"/> from disk. Cheap enough (single-digit milliseconds for a
    /// vault of a few thousand files) to call on every popup open rather than watching for file changes.</summary>
    public void Reindex()
    {
        _files.Clear();

        if (string.IsNullOrWhiteSpace(VaultPath) || !Directory.Exists(VaultPath))
        {
            return;
        }

        EnumerateDirectory(VaultPath);
        // Resting view (no query) shows the most-recently-touched files first. FileInfo's
        // LastWriteTime comes from the same Win32 FindFirstFile/FindNextFile buffer the directory
        // walk below already reads, so capturing it costs no extra I/O.
        _files.Sort(static (a, b) => b.LastModifiedUtc.CompareTo(a.LastModifiedUtc));
    }

    // Manual recursion (rather than Directory.EnumerateFiles(..., RecurseSubdirectories: true)) so
    // dot-directories (.obsidian, .trash, .git, ...) are skipped without ever walking into them, and
    // one inaccessible subfolder doesn't fail the whole scan.
    private void EnumerateDirectory(string directory)
    {
        string[] subdirectories;
        FileInfo[] files;
        try
        {
            subdirectories = Directory.GetDirectories(directory);
            files = new DirectoryInfo(directory).GetFiles("*.md");
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }

        foreach (var file in files)
        {
            _files.Add(new VaultFile
            {
                FullPath = file.FullName,
                RelativePath = Path.GetRelativePath(VaultPath, file.FullName),
                Name = Path.GetFileNameWithoutExtension(file.Name),
                LastModifiedUtc = file.LastWriteTimeUtc,
            });
        }

        foreach (var subdirectory in subdirectories)
        {
            var name = Path.GetFileName(subdirectory);
            if (name.Length > 0 && name[0] == '.')
            {
                continue;
            }

            EnumerateDirectory(subdirectory);
        }
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with files whose name fuzzy-matches
    /// <paramref name="query"/> (VS Code Quick Open style), ranked by score. Hand-rolled single pass,
    /// no LINQ, so keystroke-driven filtering doesn't allocate iterators/closures on the hot path.
    /// </summary>
    public void Search(string query, List<VaultFileMatch> results)
    {
        results.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            for (var i = 0; i < _files.Count; i++)
            {
                results.Add(new VaultFileMatch(_files[i], null));
            }

            return;
        }

        _matchBuffer.Clear();

        for (var i = 0; i < _files.Count; i++)
        {
            var file = _files[i];
            if (FuzzyMatcher.TryMatch(file.Name, query, out var score, _rangeScratch, _matchScratch))
            {
                _matchBuffer.Add((file, score, _rangeScratch.ToArray()));
            }
        }

        _matchBuffer.Sort(static (a, b) => b.Score.CompareTo(a.Score));

        for (var i = 0; i < _matchBuffer.Count; i++)
        {
            results.Add(new VaultFileMatch(_matchBuffer[i].File, _matchBuffer[i].Ranges));
        }
    }
}
