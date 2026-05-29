// Module entry: binds LlmOptions, registers the key-pool router + every keyed ILlmClient this
// module owns (Claude, AzureOpenAI, Mock, MAF), wires the factory + default ILlmClient, then runs
// HydrateRuntimeOverridesAsync at startup so the persisted Settings overrides survive a restart.
// The RemoteAgent provider lives in Modules.RemoteAgent and registers ITSELF as keyed "RemoteAgent".

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.SharedKernel.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgenticSdlc.Modules.Llm;

public sealed class LlmModule : IModule, IInitializableModule
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

        // Mock + MAF — keyed ILlmClient under canonical names.
        services.AddKeyedTransient<ILlmClient, MockLlmClient>("Mock");
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

        services.AddSingleton<ILlmClientFactory, LlmClientFactory>();
        services.AddTransient<ILlmClient>(sp => sp.GetRequiredService<ILlmClientFactory>().CreateDefault());
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        await services.HydrateRuntimeOverridesAsync(ct).ConfigureAwait(false);
    }

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
