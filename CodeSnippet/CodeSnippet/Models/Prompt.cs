namespace CodeSnippet.Models;

public class Prompt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string[]? Tags { get; set; }

    public int UsageCount { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
