// Tenant-aware IRuntimeOverrides impl. Singleton in DI, but EVERY property access resolves the
// current ITenantContext (via a per-call DI scope) and reads / writes through IAppConfigStore.
//
// EfAppConfigStore is already tenant-scoped (its EF query filters by TenantId) and caches per
// (tenant, key) for 15s, so the sync-over-async bridge here is hit at most once per 15s per
// (tenant, key, property) — not on every LLM call. Setters write through to the store AND warm
// the local 15s cache by re-reading; the Settings UI's next save lands in the right tenant row
// because the scope's ITenantContext.TenantId resolves to the calling user's tenant.
//
// The interface keeps its sync getter/setter shape so existing call sites (LlmModule pool
// factories, GitHubPrService) don't need to become async. The previous in-memory singleton
// implementation leaked tenant A's keys to tenant B; this one cannot.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.AppConfig;
using Microsoft.Extensions.DependencyInjection;

namespace AgentOs.Modules.Llm;

/// <inheritdoc cref="IRuntimeOverrides"/>
public sealed class RuntimeOverrides : IRuntimeOverrides
{
    internal const string KeyForceProvider = "Llm:ForceProvider";
    internal const string KeyAnthropicKey = "Llm:Claude:ApiKey";
    internal const string KeyAnthropicKeys = "Llm:Claude:ApiKeys";
    internal const string KeyAzureKey = "Llm:AzureOpenAi:ApiKey";
    internal const string KeyAzureKeys = "Llm:AzureOpenAi:ApiKeys";
    internal const string KeyAzureEndpoint = "Llm:AzureOpenAi:Endpoint";
    internal const string KeyGitHubPat = "Github:Pat";
    internal const string KeyGitHubOwner = "Github:RepoOwner";
    internal const string KeyGitHubRepo = "Github:RepoName";
    internal const string KeyGitHubBranch = "Github:BaseBranch";
    internal const string KeyGitHubBaseUrl = "Github:BaseUrl";

    private static readonly char[] KeyPoolSeparators = ['\n', '\r', ','];

    private readonly IServiceProvider _rootProvider;

    public RuntimeOverrides(IServiceProvider rootProvider)
    {
        ArgumentNullException.ThrowIfNull(rootProvider);
        _rootProvider = rootProvider;
    }

    public string? ForceProvider { get => Read(KeyForceProvider); set => Write(KeyForceProvider, value); }
    public string? AnthropicApiKey { get => Read(KeyAnthropicKey); set => Write(KeyAnthropicKey, value); }
    public string? AzureApiKey { get => Read(KeyAzureKey); set => Write(KeyAzureKey, value); }
    public string? AzureEndpoint { get => Read(KeyAzureEndpoint); set => Write(KeyAzureEndpoint, value); }
    public string? GitHubPat { get => Read(KeyGitHubPat); set => Write(KeyGitHubPat, value); }
    public string? GitHubRepoOwner { get => Read(KeyGitHubOwner); set => Write(KeyGitHubOwner, value); }
    public string? GitHubRepoName { get => Read(KeyGitHubRepo); set => Write(KeyGitHubRepo, value); }
    public string? GitHubBaseBranch { get => Read(KeyGitHubBranch); set => Write(KeyGitHubBranch, value); }
    public string? GitHubBaseUrl { get => Read(KeyGitHubBaseUrl); set => Write(KeyGitHubBaseUrl, value); }

    public IReadOnlyList<string> AnthropicApiKeys
    {
        get => ParseKeys(Read(KeyAnthropicKeys));
        set => Write(KeyAnthropicKeys, value is null ? null : string.Join('\n', value));
    }

    public IReadOnlyList<string> AzureApiKeys
    {
        get => ParseKeys(Read(KeyAzureKeys));
        set => Write(KeyAzureKeys, value is null ? null : string.Join('\n', value));
    }

    public ValueTask<IReadOnlyList<string>> GetAnthropicApiKeysAsync(CancellationToken cancellationToken = default)
        => GetMergedKeysAsync(KeyAnthropicKey, KeyAnthropicKeys, cancellationToken);

    public ValueTask<IReadOnlyList<string>> GetAzureApiKeysAsync(CancellationToken cancellationToken = default)
        => GetMergedKeysAsync(KeyAzureKey, KeyAzureKeys, cancellationToken);

    // Async read of (single key + key pool), merged + de-duped — no sync-over-async bridge, so the LLM
    // hot path never blocks a threadpool thread on a cache-miss. Mirrors the sync getters' merge shape.
    private async ValueTask<IReadOnlyList<string>> GetMergedKeysAsync(string singleKeyName, string poolKeyName, CancellationToken cancellationToken)
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetService<IAppConfigStore>();
        if (store is null) { return Array.Empty<string>(); }
        var single = await store.GetAsync(singleKeyName, cancellationToken).ConfigureAwait(false);
        var poolRaw = await store.GetAsync(poolKeyName, cancellationToken).ConfigureAwait(false);
        return Merge(single, ParseKeys(poolRaw));
    }

    private static List<string> Merge(string? singleKey, IEnumerable<string> pool)
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(singleKey)) { keys.Add(singleKey!); }
        keys.AddRange(pool.Where(k => !string.IsNullOrWhiteSpace(k)));
        return keys.Distinct(StringComparer.Ordinal).ToList();
    }

    private string? Read(string key)
    {
        using var scope = _rootProvider.CreateScope();
        var store = scope.ServiceProvider.GetService<IAppConfigStore>();
        if (store is null) { return null; }
        return store.GetAsync(key).AsTask().GetAwaiter().GetResult();
    }

    private void Write(string key, string? value)
    {
        using var scope = _rootProvider.CreateScope();
        var store = scope.ServiceProvider.GetService<IAppConfigStore>();
        if (store is null) { return; }
        if (string.IsNullOrWhiteSpace(value))
        {
            store.DeleteAsync(key).AsTask().GetAwaiter().GetResult();
        }
        else
        {
            store.SetAsync(key, value).AsTask().GetAwaiter().GetResult();
        }
    }

    private static List<string> ParseKeys(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return new List<string>(); }
        return value
            .Split(KeyPoolSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
