// Runtime overrides for LLM + GitHub configuration. Reads/writes are TENANT-scoped via the
// backing IAppConfigStore — each tenant carries its own LLM keys, GitHub PAT, etc. The
// interface keeps the property getter/setter shape so LlmModule's key-pool factories and
// GitHubPrService stay synchronous; the impl bridges to the async store with a per-call DI
// scope and the store's own 15s cache.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Modules.Llm;

/// <summary>Tenant-scoped runtime overrides for the LLM gateway + GitHub integration. Implementations
/// read / write through <c>IAppConfigStore</c>, so every property reflects the *current request's*
/// tenant (resolved from <c>ITenantContext</c>).</summary>
public interface IRuntimeOverrides
{
    /// <summary>The merged Anthropic key pool (single key + pool), read without blocking a threadpool
    /// thread. Use this on the LLM hot path instead of the sync <see cref="AnthropicApiKey"/> /
    /// <see cref="AnthropicApiKeys"/> getters, which bridge sync-over-async to the backing store.</summary>
    ValueTask<IReadOnlyList<string>> GetAnthropicApiKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>The merged Azure OpenAI key pool (single key + pool), read without blocking a threadpool
    /// thread. The async counterpart to <see cref="AzureApiKey"/> / <see cref="AzureApiKeys"/>.</summary>
    ValueTask<IReadOnlyList<string>> GetAzureApiKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>Overrides <c>Llm:ForceProvider</c>. When set, every agent uses this provider.</summary>
    string? ForceProvider { get; set; }

    /// <summary>Overrides <c>Llm:Claude:ApiKey</c>.</summary>
    string? AnthropicApiKey { get; set; }

    /// <summary>Tenant Anthropic key pool (newline / comma separated when persisted).</summary>
    System.Collections.Generic.IReadOnlyList<string> AnthropicApiKeys { get; set; }

    /// <summary>Overrides <c>Llm:AzureOpenAi:ApiKey</c>.</summary>
    string? AzureApiKey { get; set; }

    /// <summary>Tenant Azure OpenAI key pool.</summary>
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

    /// <summary>GitHub Enterprise base URL, e.g. <c>https://github.acme.com/api/v3</c>. Empty /
    /// null means the public github.com host. Settable per tenant.</summary>
    string? GitHubBaseUrl { get; set; }
}
