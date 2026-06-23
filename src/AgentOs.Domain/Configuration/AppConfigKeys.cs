// The canonical names of the per-tenant runtime settings stored in the encrypted app_config store.
// One source so the three places that touch these keys — RuntimeOverrides (reads/writes, Modules.Llm),
// SettingsKeyRegistry (the /settings allowlist + validation, Modules.AppConfig) and the Settings UI
// (Modules-free, Web) — cannot drift: a rename here is a single edit, not a silent three-way desync
// where the UI saves a key the registry rejects or the gateway never reads.

namespace AgentOs.Domain.Configuration;

/// <summary>Canonical app_config key names for the LLM gateway + GitHub auto-PR runtime overrides.</summary>
public static class AppConfigKeys
{
    /// <summary>Force a single provider for every agent (<c>Claude</c>/<c>AzureOpenAI</c>/<c>MAF</c>/…).</summary>
    public const string LlmForceProvider = "Llm:ForceProvider";

    /// <summary>Single Anthropic API key.</summary>
    public const string LlmClaudeApiKey = "Llm:Claude:ApiKey";

    /// <summary>Newline/comma-separated pool of Anthropic API keys (round-robin + 429 failover).</summary>
    public const string LlmClaudeApiKeys = "Llm:Claude:ApiKeys";

    /// <summary>Single Azure OpenAI API key.</summary>
    public const string LlmAzureApiKey = "Llm:AzureOpenAi:ApiKey";

    /// <summary>Pool of Azure OpenAI API keys.</summary>
    public const string LlmAzureApiKeys = "Llm:AzureOpenAi:ApiKeys";

    /// <summary>Azure OpenAI endpoint URL.</summary>
    public const string LlmAzureEndpoint = "Llm:AzureOpenAi:Endpoint";

    /// <summary>GitHub personal access token (Pipeline auto-PR).</summary>
    public const string GithubPat = "Github:Pat";

    /// <summary>GitHub repo owner for the Pipeline auto-PR target.</summary>
    public const string GithubRepoOwner = "Github:RepoOwner";

    /// <summary>GitHub repo name for the Pipeline auto-PR target.</summary>
    public const string GithubRepoName = "Github:RepoName";

    /// <summary>Base branch the auto-PR targets.</summary>
    public const string GithubBaseBranch = "Github:BaseBranch";

    /// <summary>Optional GitHub Enterprise base URL.</summary>
    public const string GithubBaseUrl = "Github:BaseUrl";
}
