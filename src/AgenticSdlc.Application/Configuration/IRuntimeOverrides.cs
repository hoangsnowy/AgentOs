// AgenticSdlc.Application/Configuration/IRuntimeOverrides.cs
// Runtime (in-memory) overrides for LLM configuration, set from the UI Settings page.
// Take precedence over appsettings.json values; live for the lifetime of the process.

namespace AgenticSdlc.Application.Configuration;

/// <summary>
/// Runtime overrides for the LLM gateway. Values set here take precedence over the corresponding
/// <c>appsettings.json</c> / user-secrets values until the process restarts.
/// </summary>
public interface IRuntimeOverrides
{
    /// <summary>
    /// Overrides <c>Llm:ForceProvider</c>. When set (e.g. <c>"AzureOpenAI"</c>), every agent uses
    /// this provider regardless of per-agent configuration. Empty/null = no override.
    /// </summary>
    string? ForceProvider { get; set; }

    /// <summary>Overrides <c>Llm:Claude:ApiKey</c>. Empty/null = no override.</summary>
    string? AnthropicApiKey { get; set; }

    /// <summary>Overrides <c>Llm:AzureOpenAi:ApiKey</c>. Empty/null = no override.</summary>
    string? AzureApiKey { get; set; }

    /// <summary>Overrides <c>Llm:AzureOpenAi:Endpoint</c>. Empty/null = no override.</summary>
    string? AzureEndpoint { get; set; }

    /// <summary>GitHub Personal Access Token (scope: <c>repo</c>). Used by <c>IGitHubPrService</c>.</summary>
    string? GitHubPat { get; set; }

    /// <summary>Target repository owner (user or org).</summary>
    string? GitHubRepoOwner { get; set; }

    /// <summary>Target repository name.</summary>
    string? GitHubRepoName { get; set; }

    /// <summary>Base branch the generated PR is opened against (default: <c>main</c>).</summary>
    string? GitHubBaseBranch { get; set; }
}
