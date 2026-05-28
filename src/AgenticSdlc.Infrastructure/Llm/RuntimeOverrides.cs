// AgenticSdlc.Infrastructure/Llm/RuntimeOverrides.cs
// In-memory implementation of IRuntimeOverrides. Registered as a singleton so changes
// from the UI Settings page survive across requests until the process restarts.

using AgenticSdlc.Application.Configuration;

namespace AgenticSdlc.Infrastructure.Llm;

/// <inheritdoc cref="IRuntimeOverrides"/>
public sealed class RuntimeOverrides : IRuntimeOverrides
{
    public string? ForceProvider { get; set; }
    public string? AnthropicApiKey { get; set; }
    public string? AzureApiKey { get; set; }
    public string? AzureEndpoint { get; set; }
}
