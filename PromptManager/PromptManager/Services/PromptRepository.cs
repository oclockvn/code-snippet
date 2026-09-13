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
            var envelope = JsonSerializer.Deserialize(json, PromptJsonContext.Default.PromptExportEnvelope);
            if (envelope?.Prompts is { } prompts)
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
        prompt.CreatedAt = DateTime.UtcNow;
        prompt.UpdatedAt = prompt.CreatedAt;
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

        prompt.UpdatedAt = DateTime.UtcNow;
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

    public void TouchUsage(Guid id)
    {
        var prompt = _prompts.Find(p => p.Id == id);
        if (prompt is null)
        {
            return;
        }

        prompt.UseCount++;
        QueueSave();
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with prompts matching <paramref name="query"/>,
    /// title matches ranked before body matches. Hand-rolled single pass, no LINQ, so keystroke-driven
    /// filtering doesn't allocate iterators/closures on the hot path.
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
            else if (prompt.Body.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _bodyMatchBuffer.Add(prompt);
            }
        }

        results.AddRange(_titleMatchBuffer);
        results.AddRange(_bodyMatchBuffer);
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

    public string ExportToJson()
    {
        var envelope = new PromptExportEnvelope { ExportedAt = DateTime.UtcNow, Prompts = new List<Prompt>(_prompts) };
        return JsonSerializer.Serialize(envelope, PromptJsonContext.Default.PromptExportEnvelope);
    }

    public static PromptExportEnvelope? ParseImport(string json) =>
        JsonSerializer.Deserialize(json, PromptJsonContext.Default.PromptExportEnvelope);

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

        var envelope = new PromptExportEnvelope { ExportedAt = DateTime.UtcNow, Prompts = snapshot };
        var json = JsonSerializer.Serialize(envelope, PromptJsonContext.Default.PromptExportEnvelope);

        var tempPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
