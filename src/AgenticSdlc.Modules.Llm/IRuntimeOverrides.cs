// Runtime (in-memory) overrides for LLM configuration, set from the UI Settings page. Take
// precedence over appsettings.json values; live for the lifetime of the process.

namespace AgenticSdlc.Modules.Llm;

/// <summary>Runtime overrides for the LLM gateway.</summary>
public interface IRuntimeOverrides
{
    /// <summary>Overrides <c>Llm:ForceProvider</c>. When set, every agent uses this provider.</summary>
    string? ForceProvider { get; set; }

    /// <summary>Overrides <c>Llm:Claude:ApiKey</c>.</summary>
    string? AnthropicApiKey { get; set; }

    /// <summary>DB-backed Anthropic key pool, hydrated from <c>IAppConfigStore</c>.</summary>
    System.Collections.Generic.IReadOnlyList<string> AnthropicApiKeys { get; set; }

    /// <summary>Overrides <c>Llm:AzureOpenAi:ApiKey</c>.</summary>
    string? AzureApiKey { get; set; }

    /// <summary>DB-backed Azure OpenAI key pool.</summary>
    System.Collections.Generic.IReadOnlyList<string> AzureApiKeys { get; set; }

    /// <summary>Overrides <c>Llm:AzureOpenAi:Endpoint</c>.</summary>
    string? AzureEndpoint { get; set; }

    /// <summary>GitHub Personal Access Token (scope: <c>repo</c>).</summary>
    string? GitHubPat { get; set; }

    /// <summary>Target repository owner.</summary>
    string? GitHubRepoOwner { get; set; }

    /// <summary>Target repository name.</summary>
    string? GitHubRepoName { get; set; }

    /// <summary>Base branch the generated PR is opened against (default: <c>main</c>).</summary>
    string? GitHubBaseBranch { get; set; }
}
