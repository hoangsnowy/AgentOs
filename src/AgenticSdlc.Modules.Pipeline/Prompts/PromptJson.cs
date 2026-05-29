// AgenticSdlc.Application/Prompts/PromptJson.cs
// Shared JsonSerializerOptions for every prompt renderer (camelCase, indented, unicode-safe).

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace AgenticSdlc.Modules.Pipeline.Prompts;

/// <summary>Standard JSON options used when rendering a user prompt (camelCase, indented, no unicode escaping).</summary>
public static class PromptJson
{
    /// <summary>Default options used for every prompt.</summary>
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };
}
