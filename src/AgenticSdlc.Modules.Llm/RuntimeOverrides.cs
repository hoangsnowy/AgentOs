// In-memory implementation of IRuntimeOverrides. Registered as a singleton so changes from the UI
// Settings page survive across requests until the process restarts.

using System;
using System.Collections.Generic;

namespace AgenticSdlc.Modules.Llm;

/// <inheritdoc cref="IRuntimeOverrides"/>
public sealed class RuntimeOverrides : IRuntimeOverrides
{
    public string? ForceProvider { get; set; }
    public string? AnthropicApiKey { get; set; }
    public IReadOnlyList<string> AnthropicApiKeys { get; set; } = Array.Empty<string>();
    public string? AzureApiKey { get; set; }
    public IReadOnlyList<string> AzureApiKeys { get; set; } = Array.Empty<string>();
    public string? AzureEndpoint { get; set; }
    public string? GitHubPat { get; set; }
    public string? GitHubRepoOwner { get; set; }
    public string? GitHubRepoName { get; set; }
    public string? GitHubBaseBranch { get; set; }
}
