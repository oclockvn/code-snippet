namespace PromptManager.Models;

public class PromptExportEnvelope
{
    public int SchemaVersion { get; set; } = 1;

    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    public List<Prompt> Prompts { get; set; } = new();
}
