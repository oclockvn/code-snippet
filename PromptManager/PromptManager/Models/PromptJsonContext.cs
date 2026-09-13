using System.Text.Json.Serialization;

namespace PromptManager.Models;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PromptExportEnvelope))]
[JsonSerializable(typeof(AppSettings))]
internal partial class PromptJsonContext : JsonSerializerContext
{
}
