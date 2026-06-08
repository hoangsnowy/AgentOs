// Module entry: binds LlmOptions, registers the key-pool router + every keyed ILlmClient this
// module owns (Claude, AzureOpenAI, MAF), wires the factory + default ILlmClient. The runtime
// overrides are tenant-scoped (RuntimeOverrides reads through IAppConfigStore on every access,
// using the current request's ITenantContext), so no startup hydration step is needed.
// The RemoteAgent provider lives in Modules.RemoteAgent and registers ITSELF as keyed "RemoteAgent".

using System;
using System.Collections.Generic;
using System.Linq;
using AgentOs.Domain.Llm;
using AgentOs.SharedKernel.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgentOs.Modules.Llm;

public sealed class LlmModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ApiKeyRouter>();
        services.AddSingleton<IRuntimeOverrides, RuntimeOverrides>();

        // MAF — keyed ILlmClient under canonical name.
        services.AddKeyedTransient<ILlmClient, MafChatClient>("MAF");

        // Claude (Anthropic.SDK) + Azure OpenAI (Azure.AI.OpenAI) — pooled keyed clients with
        // round-robin + rate-limit failover across the (runtime override + appsettings) key pool.
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
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledChatLlmClient>>(),
                baseDelay: null,
                toolRegistry: sp.GetService<AgentOs.Domain.Tools.IToolRegistry>(),
                tenantContext: sp.GetService<AgentOs.SharedKernel.Identity.ITenantContext>(),
                toolPolicy: sp.GetService<AgentOs.Domain.Tools.IToolPolicy>(),
                toolInvocationLog: sp.GetService<AgentOs.Domain.Tools.IToolInvocationLog>());
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
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledChatLlmClient>>(),
                baseDelay: null,
                toolRegistry: sp.GetService<AgentOs.Domain.Tools.IToolRegistry>(),
                tenantContext: sp.GetService<AgentOs.SharedKernel.Identity.ITenantContext>(),
                toolPolicy: sp.GetService<AgentOs.Domain.Tools.IToolPolicy>(),
                toolInvocationLog: sp.GetService<AgentOs.Domain.Tools.IToolInvocationLog>());
        });

        services.AddSingleton<ILlmClientFactory, LlmClientFactory>();
        services.AddTransient<ILlmClient>(sp => sp.GetRequiredService<ILlmClientFactory>().CreateDefault());
    }

    // Tenant-isolated key resolution. When the current tenant has configured ITS OWN key(s) via the
    // Settings UI (stored per-tenant in app_config), use ONLY those — the shared appsettings/env platform
    // key is NEVER appended, so tenant A can never silently spend on the platform key (or tenant B's).
    // The platform key is a dev/demo fallback used ONLY when the tenant has no key of its own.
    private static List<string> ClaudeKeyPool(ClaudeOptions opts, IRuntimeOverrides ov)
    {
        var tenantKeys = Collect(ov.AnthropicApiKey, ov.AnthropicApiKeys);
        return tenantKeys.Count > 0 ? tenantKeys : Collect(opts.ApiKey, opts.ApiKeys);
    }

    private static List<string> AzureKeyPool(AzureOpenAiOptions opts, IRuntimeOverrides ov)
    {
        var tenantKeys = Collect(ov.AzureApiKey, ov.AzureApiKeys);
        return tenantKeys.Count > 0 ? tenantKeys : Collect(opts.ApiKey, opts.ApiKeys);
    }

    private static List<string> Collect(string? singleKey, IEnumerable<string> pool)
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(singleKey)) { keys.Add(singleKey!); }
        keys.AddRange(pool.Where(k => !string.IsNullOrWhiteSpace(k)));
        return keys.Distinct(StringComparer.Ordinal).ToList();
    }
}
