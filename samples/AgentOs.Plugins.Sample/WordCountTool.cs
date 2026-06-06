// The tool the sample plugin contributes. Pure, dependency-free (BCL only) — counts words + characters
// in a text string. Registered via the standard ITool seam, so it flows through IToolGateway (policy +
// evidence) exactly like a first-party tool.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;

namespace AgentOs.Plugins.Sample;

/// <summary>Counts words + characters in a <c>text</c> input.</summary>
public sealed class WordCountTool : ITool
{
    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new(
        Name: "word_count",
        Description: "Counts the words and characters in a text string.",
        JsonInputSchema: """{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}""");

    /// <inheritdoc />
    public Task<ToolInvocationResult> InvokeAsync(ToolInvocationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Input) ? "{}" : request.Input);
            var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            var output = JsonSerializer.Serialize(new { words, characters = text.Length });
            return Task.FromResult(ToolInvocationResult.Success(request.CallId, output));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(ToolInvocationResult.Error(request.CallId, $"Invalid input JSON: {ex.Message}"));
        }
    }
}
