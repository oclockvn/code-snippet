using CodeSnippet.Models;
using System.Text.Json.Serialization;

namespace CodeSnippet.Models;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<Prompt>))]
[JsonSerializable(typeof(AppSettings))]
internal partial class PromptJsonContext : JsonSerializerContext
{
}
