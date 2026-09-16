using System.IO;
using System.Text.Json;
using CodeSnippet.Models;
using CodeSnippet.Models;

namespace CodeSnippet.Services;

/// <summary>One search result: the matched prompt, plus its title's fuzzy-match highlight ranges
/// (null for a body/tag match, or when there was no query).</summary>
public readonly record struct PromptMatch(Prompt Prompt, IReadOnlyList<(int Start, int Length)>? TitleRanges);

/// <summary>
/// In-memory prompt store backed by a flat JSON file. All reads and mutations are synchronous
/// against the in-memory list (sub-millisecond at the dataset sizes this app targets); persistence
/// to disk always happens on a background task so no UI-triggered action ever waits on I/O.
/// </summary>
public sealed class PromptRepository
{
    private readonly string _filePath;
    private readonly List<Prompt> _prompts = new();
    private readonly List<(Prompt Prompt, int Score, (int Start, int Length)[] Ranges)> _titleMatchBuffer = new();
    private readonly List<Prompt> _bodyMatchBuffer = new();
    private readonly List<(Prompt Prompt, int Score)> _tagMatchBuffer = new();
    private readonly List<(int Start, int Length)> _rangeScratch = new();
    private readonly FuzzyMatcher.Scratch _matchScratch = new();

    private readonly object _saveGate = new();
    private List<Prompt>? _pendingSnapshot;
    private bool _isSaving;

    public PromptRepository()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodeSnippet", "prompts.json"))
    {
    }

    public PromptRepository(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyList<Prompt> Prompts => _prompts;

    public string FilePath => _filePath;

    public event Action<Exception>? SaveFailed;

    public void Load()
    {
        _prompts.Clear();
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var prompts = JsonSerializer.Deserialize(json, PromptJsonContext.Default.ListPrompt);
            if (prompts is not null)
            {
                _prompts.AddRange(prompts);
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable file: start from an empty set rather than failing app startup.
        }
    }

    public void Add(Prompt prompt)
    {
        _prompts.Add(prompt);
        QueueSave();
    }

    public bool Update(Prompt prompt)
    {
        var index = _prompts.FindIndex(p => p.Id == prompt.Id);
        if (index < 0)
        {
            return false;
        }

        _prompts[index] = prompt;
        QueueSave();
        return true;
    }

    public bool Delete(Guid id)
    {
        var removed = _prompts.RemoveAll(p => p.Id == id) > 0;
        if (removed)
        {
            QueueSave();
        }

        return removed;
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with prompts matching <paramref name="query"/>.
    /// Titles are fuzzy-matched (VS Code Quick Open style) and ranked by score, above any body/tag
    /// substring matches. Hand-rolled single pass, no LINQ, so keystroke-driven filtering doesn't
    /// allocate iterators/closures on the hot path. Each title match's highlight ranges are captured
    /// once here (via a pooled DP scratch buffer) rather than recomputed later for display.
    /// </summary>
    public void Search(string query, List<PromptMatch> results)
    {
        results.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            for (var i = 0; i < _prompts.Count; i++)
            {
                results.Add(new PromptMatch(_prompts[i], null));
            }

            return;
        }

        _titleMatchBuffer.Clear();
        _bodyMatchBuffer.Clear();

        for (var i = 0; i < _prompts.Count; i++)
        {
            var prompt = _prompts[i];
            if (FuzzyMatcher.TryMatch(prompt.Title, query, out var score, _rangeScratch, _matchScratch))
            {
                _titleMatchBuffer.Add((prompt, score, _rangeScratch.ToArray()));
            }
            else if (prompt.Body.Contains(query, StringComparison.OrdinalIgnoreCase) || MatchesAnyTag(prompt.Tags, query))
            {
                _bodyMatchBuffer.Add(prompt);
            }
        }

        _titleMatchBuffer.Sort(static (a, b) => b.Score.CompareTo(a.Score));

        for (var i = 0; i < _titleMatchBuffer.Count; i++)
        {
            results.Add(new PromptMatch(_titleMatchBuffer[i].Prompt, _titleMatchBuffer[i].Ranges));
        }

        for (var i = 0; i < _bodyMatchBuffer.Count; i++)
        {
            results.Add(new PromptMatch(_bodyMatchBuffer[i], null));
        }
    }

    private static bool MatchesAnyTag(string[]? tags, string query)
    {
        if (tags is null)
        {
            return false;
        }

        for (var i = 0; i < tags.Length; i++)
        {
            if (tags[i].Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with prompts having at least one tag that
    /// fuzzy-matches any of <paramref name="tagTokens"/> (same VS Code Quick Open-style matcher used
    /// for titles, so "re" matches both "review" and "refactor"), OR'd across tags and tokens. Ranked
    /// by each prompt's best tag-match score, highest first. Used for `#tag` queries, where tag
    /// filtering replaces title/body search entirely. Empty tokens means no filter yet (e.g. a bare
    /// "#"), so every prompt is returned unranked.
    /// </summary>
    public void SearchByTags(IReadOnlyList<string> tagTokens, List<PromptMatch> results)
    {
        results.Clear();

        if (tagTokens.Count == 0)
        {
            for (var i = 0; i < _prompts.Count; i++)
            {
                results.Add(new PromptMatch(_prompts[i], null));
            }

            return;
        }

        _tagMatchBuffer.Clear();

        for (var i = 0; i < _prompts.Count; i++)
        {
            var prompt = _prompts[i];
            if (TryBestTagScore(prompt.Tags, tagTokens, out var score))
            {
                _tagMatchBuffer.Add((prompt, score));
            }
        }

        _tagMatchBuffer.Sort(static (a, b) => b.Score.CompareTo(a.Score));

        for (var i = 0; i < _tagMatchBuffer.Count; i++)
        {
            results.Add(new PromptMatch(_tagMatchBuffer[i].Prompt, null));
        }
    }

    private bool TryBestTagScore(string[]? tags, IReadOnlyList<string> tagTokens, out int bestScore)
    {
        bestScore = int.MinValue;
        var found = false;

        if (tags is null)
        {
            return false;
        }

        for (var i = 0; i < tags.Length; i++)
        {
            for (var j = 0; j < tagTokens.Count; j++)
            {
                if (FuzzyMatcher.TryMatch(tags[i], tagTokens[j], out var score, _rangeScratch, _matchScratch) && score > bestScore)
                {
                    found = true;
                    bestScore = score;
                }
            }
        }

        return found;
    }

    /// <summary>Upserts by Id. Returns the number of prompts processed.</summary>
    public int Import(IEnumerable<Prompt> incoming)
    {
        var count = 0;
        foreach (var prompt in incoming)
        {
            var index = _prompts.FindIndex(p => p.Id == prompt.Id);
            if (index >= 0)
            {
                _prompts[index] = prompt;
            }
            else
            {
                _prompts.Add(prompt);
            }

            count++;
        }

        if (count > 0)
        {
            QueueSave();
        }

        return count;
    }

    public string ExportToJson() =>
        JsonSerializer.Serialize(_prompts, PromptJsonContext.Default.ListPrompt);

    public static List<Prompt>? ParseImport(string json) =>
        JsonSerializer.Deserialize(json, PromptJsonContext.Default.ListPrompt);

    private void QueueSave()
    {
        lock (_saveGate)
        {
            _pendingSnapshot = new List<Prompt>(_prompts);
            if (_isSaving)
            {
                return;
            }

            _isSaving = true;
        }

        _ = Task.Run(SaveLoopAsync);
    }

    // Only ever writes the most recently queued snapshot; if new changes arrive while a write is in
    // flight, one more write follows with the latest state instead of writing every intermediate state.
    private async Task SaveLoopAsync()
    {
        while (true)
        {
            List<Prompt> snapshot;
            lock (_saveGate)
            {
                snapshot = _pendingSnapshot!;
                _pendingSnapshot = null;
            }

            try
            {
                await WriteSnapshotAsync(snapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SaveFailed?.Invoke(ex);
            }

            lock (_saveGate)
            {
                if (_pendingSnapshot is null)
                {
                    _isSaving = false;
                    return;
                }
            }
        }
    }

    private async Task WriteSnapshotAsync(List<Prompt> snapshot)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(snapshot, PromptJsonContext.Default.ListPrompt);

        var tempPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
