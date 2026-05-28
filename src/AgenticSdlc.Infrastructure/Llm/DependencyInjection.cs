// AgenticSdlc.Infrastructure/Llm/DependencyInjection.cs
// Sprint 1 — Extension that registers the LLM Gateway into IServiceCollection.

using System;
using AgenticSdlc.Application.Configuration;
using AgenticSdlc.Domain.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        // Named HttpClient — IHttpClientFactory to avoid socket exhaustion.
        services.AddHttpClient(ClaudeClient.HttpClientName, (sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value.Claude;
            if (!string.IsNullOrWhiteSpace(opts.Endpoint))
            {
                http.BaseAddress = new Uri(opts.Endpoint.TrimEnd('/') + "/");
            }
            if (opts.TimeoutSeconds > 0)
            {
                http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            }
        });

        services.AddHttpClient(AzureOpenAiClient.HttpClientName, (sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value.AzureOpenAi;
            if (!string.IsNullOrWhiteSpace(opts.Endpoint))
            {
                http.BaseAddress = new Uri(opts.Endpoint.TrimEnd('/') + "/");
            }
            if (opts.TimeoutSeconds > 0)
            {
                http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            }
        });

        // Concrete clients — singleton-ish, but kept Transient so each resolve gets a fresh HttpClient from the factory.
        services.AddTransient<ClaudeClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(ClaudeClient.HttpClientName);
            var opts = sp.GetRequiredService<IOptions<LlmOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClaudeClient>>();
            return new ClaudeClient(http, opts, logger);
        });

        services.AddTransient<AzureOpenAiClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(AzureOpenAiClient.HttpClientName);
            var opts = sp.GetRequiredService<IOptions<LlmOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AzureOpenAiClient>>();
            return new AzureOpenAiClient(http, opts, logger);
        });

        services.AddTransient<MockLlmClient>();
        services.AddTransient<MafChatClient>();   // Azure OpenAI via official SDK + Microsoft.Extensions.AI

        // Runtime overrides (in-memory) — settable from the Settings UI; take precedence over LlmOptions.
        services.AddSingleton<IRuntimeOverrides, RuntimeOverrides>();

        // Factory + default ILlmClient.
        services.AddSingleton<ILlmClientFactory, LlmClientFactory>();
        services.AddTransient<ILlmClient>(sp => sp.GetRequiredService<ILlmClientFactory>().CreateDefault());

        return services;
    }
}
