// Bridge between IAppConfigStore (AppConfig module) and IRuntimeOverrides (this module). Hydrate
// is called at startup so a process restart keeps the operator's last-saved keys; Save persists
// the in-memory overrides back into the store after a Settings UI update.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.AppConfig;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticSdlc.Modules.Llm;

/// <summary>DI helpers that bridge the AppConfig store and the LLM runtime overrides.</summary>
public static class RuntimeOverridesExtensions
{
    private const string KeyForceProvider = "Llm:ForceProvider";
    private const string KeyAnthropicKey = "Llm:Claude:ApiKey";
    private const string KeyAnthropicKeys = "Llm:Claude:ApiKeys";
    private const string KeyAzureKey = "Llm:AzureOpenAi:ApiKey";
    private const string KeyAzureKeys = "Llm:AzureOpenAi:ApiKeys";
    private const string KeyAzureEndpoint = "Llm:AzureOpenAi:Endpoint";
    private const string KeyGitHubPat = "Github:Pat";
    private const string KeyGitHubOwner = "Github:RepoOwner";
    private const string KeyGitHubRepo = "Github:RepoName";
    private const string KeyGitHubBranch = "Github:BaseBranch";

    private static readonly char[] KeyPoolSeparators = ['\n', '\r', ','];

    /// <summary>Hydrate <see cref="IRuntimeOverrides"/> from the persisted <see cref="IAppConfigStore"/>.</summary>
    public static async Task HydrateRuntimeOverridesAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetService<IAppConfigStore>();
        var overrides = scope.ServiceProvider.GetService<IRuntimeOverrides>();
        if (store is null || overrides is null)
        {
            return;
        }

        overrides.ForceProvider = await store.GetAsync(KeyForceProvider, ct).ConfigureAwait(false) ?? overrides.ForceProvider;
        overrides.AnthropicApiKey = await store.GetAsync(KeyAnthropicKey, ct).ConfigureAwait(false) ?? overrides.AnthropicApiKey;
        overrides.AnthropicApiKeys = ParseKeys(await store.GetAsync(KeyAnthropicKeys, ct).ConfigureAwait(false)) is { Count: > 0 } ak ? ak : overrides.AnthropicApiKeys;
        overrides.AzureApiKey = await store.GetAsync(KeyAzureKey, ct).ConfigureAwait(false) ?? overrides.AzureApiKey;
        overrides.AzureApiKeys = ParseKeys(await store.GetAsync(KeyAzureKeys, ct).ConfigureAwait(false)) is { Count: > 0 } zk ? zk : overrides.AzureApiKeys;
        overrides.AzureEndpoint = await store.GetAsync(KeyAzureEndpoint, ct).ConfigureAwait(false) ?? overrides.AzureEndpoint;
        overrides.GitHubPat = await store.GetAsync(KeyGitHubPat, ct).ConfigureAwait(false) ?? overrides.GitHubPat;
        overrides.GitHubRepoOwner = await store.GetAsync(KeyGitHubOwner, ct).ConfigureAwait(false) ?? overrides.GitHubRepoOwner;
        overrides.GitHubRepoName = await store.GetAsync(KeyGitHubRepo, ct).ConfigureAwait(false) ?? overrides.GitHubRepoName;
        overrides.GitHubBaseBranch = await store.GetAsync(KeyGitHubBranch, ct).ConfigureAwait(false) ?? overrides.GitHubBaseBranch;
    }

    /// <summary>Persist the current <see cref="IRuntimeOverrides"/> into the store.</summary>
    public static async Task SaveRuntimeOverridesAsync(this IAppConfigStore store, IRuntimeOverrides overrides, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(overrides);

        await SaveIfSet(store, KeyForceProvider, overrides.ForceProvider, ct).ConfigureAwait(false);
        await SaveIfSet(store, KeyAnthropicKey, overrides.AnthropicApiKey, ct).ConfigureAwait(false);
        await SaveIfSet(store, KeyAzureKey, overrides.AzureApiKey, ct).ConfigureAwait(false);
        await SaveIfSet(store, KeyAzureEndpoint, overrides.AzureEndpoint, ct).ConfigureAwait(false);
        await SaveIfSet(store, KeyGitHubPat, overrides.GitHubPat, ct).ConfigureAwait(false);
        await SaveIfSet(store, KeyGitHubOwner, overrides.GitHubRepoOwner, ct).ConfigureAwait(false);
        await SaveIfSet(store, KeyGitHubRepo, overrides.GitHubRepoName, ct).ConfigureAwait(false);
        await SaveIfSet(store, KeyGitHubBranch, overrides.GitHubBaseBranch, ct).ConfigureAwait(false);
    }

    private static async Task SaveIfSet(IAppConfigStore store, string key, string? value, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            await store.SetAsync(key, value, ct).ConfigureAwait(false);
        }
    }

    private static System.Collections.Generic.List<string> ParseKeys(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new System.Collections.Generic.List<string>();
        }
        return value
            .Split(KeyPoolSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
