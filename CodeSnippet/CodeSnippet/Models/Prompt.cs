namespace CodeSnippet.Models;

public class Prompt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string[]? Tags { get; set; }
}
