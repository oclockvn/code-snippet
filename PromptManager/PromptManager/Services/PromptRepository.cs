using System.IO;
using System.Text.Json;
using PromptManager.Models;

namespace PromptManager.Services;

/// <summary>
/// In-memory prompt store backed by a flat JSON file. All reads and mutations are synchronous
/// against the in-memory list (sub-millisecond at the dataset sizes this app targets); persistence
/// to disk always happens on a background task so no UI-triggered action ever waits on I/O.
/// </summary>
public sealed class PromptRepository
{
    private readonly string _filePath;
    private readonly List<Prompt> _prompts = new();
    private readonly List<Prompt> _titleMatchBuffer = new();
    private readonly List<Prompt> _bodyMatchBuffer = new();

    private readonly object _saveGate = new();
    private List<Prompt>? _pendingSnapshot;
    private bool _isSaving;

    public PromptRepository()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PromptManager", "prompts.json"))
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

    /// <summary>Marks a prompt as used right now: bumps its usage count and last-used timestamp.</summary>
    public bool RecordUsage(Guid id)
    {
        var index = _prompts.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return false;
        }

        var prompt = _prompts[index];
        prompt.UsageCount++;
        prompt.LastUsedAt = DateTimeOffset.Now;
        QueueSave();
        return true;
    }

    /// <summary>Shared ordering used by both the popup's grouping and the Manager's sort tabs.</summary>
    public static IEnumerable<Prompt> OrderBy(IEnumerable<Prompt> prompts, PromptSortMode mode) => mode switch
    {
        PromptSortMode.MostUsed => prompts.OrderByDescending(p => p.UsageCount).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase),
        PromptSortMode.AZ => prompts.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase),
        _ => prompts.OrderByDescending(p => p.LastUsedAt ?? DateTimeOffset.MinValue).ThenByDescending(p => p.CreatedAt),
    };

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with prompts matching <paramref name="query"/>,
    /// title matches ranked before body matches. Hand-rolled single pass, no LINQ, so
    /// keystroke-driven filtering doesn't allocate iterators/closures on the hot path.
    /// </summary>
    public void Search(string query, List<Prompt> results)
    {
        results.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            results.AddRange(_prompts);
            return;
        }

        _titleMatchBuffer.Clear();
        _bodyMatchBuffer.Clear();

        for (var i = 0; i < _prompts.Count; i++)
        {
            var prompt = _prompts[i];
            if (prompt.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _titleMatchBuffer.Add(prompt);
            }
            else if (prompt.Body.Contains(query, StringComparison.OrdinalIgnoreCase) || MatchesAnyTag(prompt.Tags, query))
            {
                _bodyMatchBuffer.Add(prompt);
            }
        }

        results.AddRange(_titleMatchBuffer);
        results.AddRange(_bodyMatchBuffer);
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
