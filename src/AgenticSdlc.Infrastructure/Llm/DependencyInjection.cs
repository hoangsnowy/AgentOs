// AgenticSdlc.Infrastructure/Llm/DependencyInjection.cs
// Sprint 1 — Extension that registers the LLM Gateway into IServiceCollection.

using System;
using System.Collections.Generic;
using System.Linq;
using AgenticSdlc.Application.Configuration;
using AgenticSdlc.Domain.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgenticSdlc.Infrastructure.Llm;

/// <summary>
/// DI extension for the LLM Gateway. Call once in the API project's <c>Program.cs</c>.
/// </summary>
public static class LlmGatewayServiceCollectionExtensions
{
    /// <summary>
    /// Register:
    /// <list type="bullet">
    ///   <item>Bind <see cref="LlmOptions"/> from section <c>"Llm"</c>.</item>
    ///   <item><see cref="ClaudeClient"/>, <see cref="AzureOpenAiClient"/>, <see cref="MockLlmClient"/> (concrete).</item>
    ///   <item><see cref="ILlmClientFactory"/> → <see cref="LlmClientFactory"/>.</item>
    ///   <item><see cref="ILlmClient"/> → resolved via the factory at runtime.</item>
    ///   <item>Named <c>HttpClient</c> for each provider via <see cref="IHttpClientFactory"/>.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddLlmGateway(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .ValidateOnStart();

        // Multi-key router — round-robin + rate-limit (429) failover across each provider's key pool.
        // Singleton so cooldown state is shared across the transient client instances.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ApiKeyRouter>();

        services.AddTransient<MockLlmClient>();
        services.AddTransient<MafChatClient>();   // Azure OpenAI via official SDK + Microsoft.Extensions.AI

        // SDK-based provider clients via Microsoft.Extensions.AI IChatClient: Claude (Anthropic.SDK) +
        // Azure OpenAI (Azure.AI.OpenAI), each a multi-key pool with 429 failover. These are what the
        // factory resolves for "Claude"/"AzureOpenAI"; the raw HttpClient clients above are legacy.
        services.AddKeyedSingleton<ILlmClient>("Claude", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmOptions>>();
            var ov = sp.GetRequiredService<IRuntimeOverrides>();
            return new PooledChatLlmClient(
                "Claude",
                (key, _model) => SdkChatClients.CreateClaude(key),
                () => ClaudeKeyPool(opts.Value.Claude, ov),
                sp.GetRequiredService<ApiKeyRouter>(),
                SdkChatClients.IsRateLimited,
                _ => null,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledChatLlmClient>>());
        });
        services.AddKeyedSingleton<ILlmClient>("AzureOpenAI", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmOptions>>();
            var ov = sp.GetRequiredService<IRuntimeOverrides>();
            return new PooledChatLlmClient(
                "AzureOpenAI",
                (key, model) => SdkChatClients.CreateAzure(
                    key,
                    !string.IsNullOrWhiteSpace(ov.AzureEndpoint) ? ov.AzureEndpoint! : opts.Value.AzureOpenAi.Endpoint,
                    string.IsNullOrWhiteSpace(model) ? opts.Value.AzureOpenAi.Model : model),
                () => AzureKeyPool(opts.Value.AzureOpenAi, ov),
                sp.GetRequiredService<ApiKeyRouter>(),
                SdkChatClients.IsRateLimited,
                _ => null,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledChatLlmClient>>());
        });

        // "Remote dev-IDE agent" runtime — dispatch codegen to a connected remote agent (0 API tokens).
        // Broker is a singleton (holds connected agents + pending requests); a transport (SignalR) wires to it.
        services.TryAddSingleton<RemoteAgent.IRemoteAgentBroker, RemoteAgent.InProcessRemoteAgentBroker>();
        services.TryAddSingleton<RemoteAgent.IRemoteExecApprover, RemoteAgent.AutoApproveRemoteExec>();
        services.AddTransient<RemoteAgentLlmClient>();

        // Runtime overrides (in-memory) — settable from the Settings UI; take precedence over LlmOptions.
        services.AddSingleton<IRuntimeOverrides, RuntimeOverrides>();

        // Tenant context (Epic D) — default single-tenant ("default") until OIDC wires a claims-based one.
        services.TryAddScoped<AgenticSdlc.Application.Identity.ITenantContext, Identity.DefaultTenantContext>();

        // Factory + default ILlmClient.
        services.AddSingleton<ILlmClientFactory, LlmClientFactory>();
        services.AddTransient<ILlmClient>(sp => sp.GetRequiredService<ILlmClientFactory>().CreateDefault());

        return services;
    }

    // Distinct key pool: runtime override (Settings) first, then configured ApiKeys, then the single ApiKey.
    // Pool order: runtime single override, then the DB-backed pool (IAppConfigStore, hydrated into
    // overrides), then appsettings ApiKeys, then the single appsettings ApiKey.
    private static List<string> ClaudeKeyPool(ClaudeOptions opts, IRuntimeOverrides ov)
        => Pool(ov.AnthropicApiKey, ov.AnthropicApiKeys.Concat(opts.ApiKeys), opts.ApiKey);

    private static List<string> AzureKeyPool(AzureOpenAiOptions opts, IRuntimeOverrides ov)
        => Pool(ov.AzureApiKey, ov.AzureApiKeys.Concat(opts.ApiKeys), opts.ApiKey);

    private static List<string> Pool(string? overrideKey, IEnumerable<string> pool, string singleKey)
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(overrideKey)) { keys.Add(overrideKey!); }
        foreach (var k in pool)
        {
            if (!string.IsNullOrWhiteSpace(k)) { keys.Add(k); }
        }
        if (!string.IsNullOrWhiteSpace(singleKey)) { keys.Add(singleKey); }
        return keys.Distinct(StringComparer.Ordinal).ToList();
    }
}
