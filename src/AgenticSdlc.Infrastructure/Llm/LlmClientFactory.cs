// AgenticSdlc.Infrastructure/Llm/LlmClientFactory.cs
// Sprint 1 — Factory that resolves ILlmClient by LlmOptions.Provider.

using System;
using AgenticSdlc.Domain.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgenticSdlc.Infrastructure.Llm;

/// <summary>
/// Factory that selects the <see cref="ILlmClient"/> impl based on <see cref="LlmOptions.Provider"/>.
/// 3 providers are supported: <c>"Claude"</c> (case-insensitive), <c>"AzureOpenAI"</c>, <c>"Mock"</c>.
/// </summary>
public interface ILlmClientFactory
{
    /// <summary>Returns the client for the provider configured as default in <see cref="LlmOptions"/>.</summary>
    ILlmClient CreateDefault();

    /// <summary>Returns the client for the specified provider (overrides the default configuration).</summary>
    ILlmClient Create(string providerName);
}

/// <summary>Default implementation that looks up from the DI container.</summary>
public sealed class LlmClientFactory : ILlmClientFactory
{
    private readonly IServiceProvider _services;
    private readonly LlmOptions _options;

    /// <summary>Initializes with the DI provider + options.</summary>
    public LlmClientFactory(IServiceProvider services, IOptions<LlmOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options.Value ?? new LlmOptions();
    }

    /// <inheritdoc />
    public ILlmClient CreateDefault() => Create(_options.Provider);

    /// <inheritdoc />
    public ILlmClient Create(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name must not be empty.", nameof(providerName));
        }

        return providerName.Trim().ToUpperInvariant() switch
        {
            "CLAUDE" or "ANTHROPIC" => _services.GetRequiredService<ClaudeClient>(),
            "AZUREOPENAI" or "AZURE" or "OPENAI" => _services.GetRequiredService<AzureOpenAiClient>(),
            "MOCK" or "FAKE" or "STUB" => _services.GetRequiredService<MockLlmClient>(),
            _ => throw new LlmException($"Unknown LLM provider: '{providerName}'. Expected Claude | AzureOpenAI | Mock."),
        };
    }
}
