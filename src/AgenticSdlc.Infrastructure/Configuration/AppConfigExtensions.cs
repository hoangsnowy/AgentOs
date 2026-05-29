// AgenticSdlc.Infrastructure/Configuration/AppConfigExtensions.cs
// Phase 8.4b — DI for the IAppConfigStore + startup hydration of IRuntimeOverrides from it.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgenticSdlc.Infrastructure.Configuration;

/// <summary>DI + hydration helpers for the runtime configuration store.</summary>
public static class AppConfigExtensions
{
    // app_config keys used to persist the runtime overrides.
    private const string KeyForceProvider = "Llm:ForceProvider";
    private const string KeyAnthropicKey = "Llm:Claude:ApiKey";
    private const string KeyAzureKey = "Llm:AzureOpenAi:ApiKey";
    private const string KeyAzureEndpoint = "Llm:AzureOpenAi:Endpoint";
    private const string KeyGitHubPat = "Github:Pat";
    private const string KeyGitHubOwner = "Github:RepoOwner";
    private const string KeyGitHubRepo = "Github:RepoName";
    private const string KeyGitHubBranch = "Github:BaseBranch";

    /// <summary>
    /// Registers the <see cref="IAppConfigStore"/>. When <c>ConnectionStrings:DefaultConnection</c> is
    /// present, uses the EF + DataProtection-encrypted store (caller must have called
    /// <c>AddDataProtection()</c> + <c>AddPersistence()</c>). Otherwise falls back to the in-memory store.
    /// </summary>
    public static IServiceCollection AddAppConfigStore(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        var hasDb = !string.IsNullOrWhiteSpace(config.GetConnectionString("DefaultConnection"));
        if (hasDb)
        {
            services.AddSingleton<IAppConfigStore, EfAppConfigStore>();
        }
        else
        {
            services.TryAddSingleton<IAppConfigStore, InMemoryAppConfigStore>();
        }
        return services;
    }

    /// <summary>
    /// Hydrate <see cref="IRuntimeOverrides"/> from the persisted <see cref="IAppConfigStore"/> at startup
    /// so a process restart keeps the operator's last-saved LLM keys / GitHub config. Call once after
    /// building the app: <c>await app.Services.HydrateRuntimeOverridesAsync();</c>
    /// </summary>
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
        overrides.AzureApiKey = await store.GetAsync(KeyAzureKey, ct).ConfigureAwait(false) ?? overrides.AzureApiKey;
        overrides.AzureEndpoint = await store.GetAsync(KeyAzureEndpoint, ct).ConfigureAwait(false) ?? overrides.AzureEndpoint;
        overrides.GitHubPat = await store.GetAsync(KeyGitHubPat, ct).ConfigureAwait(false) ?? overrides.GitHubPat;
        overrides.GitHubRepoOwner = await store.GetAsync(KeyGitHubOwner, ct).ConfigureAwait(false) ?? overrides.GitHubRepoOwner;
        overrides.GitHubRepoName = await store.GetAsync(KeyGitHubRepo, ct).ConfigureAwait(false) ?? overrides.GitHubRepoName;
        overrides.GitHubBaseBranch = await store.GetAsync(KeyGitHubBranch, ct).ConfigureAwait(false) ?? overrides.GitHubBaseBranch;
    }

    /// <summary>
    /// Persist the current <see cref="IRuntimeOverrides"/> into the store. The Settings UI calls this after
    /// mutating the overrides so the values survive a restart. Only non-empty values are written.
    /// </summary>
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
}
