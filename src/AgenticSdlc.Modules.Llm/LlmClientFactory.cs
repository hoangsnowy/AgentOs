// Resolves ILlmClient by provider name via keyed services. Every provider (Claude/AzureOpenAI/Mock/
// MAF/RemoteAgent) registers as a keyed ILlmClient under its canonical name; the factory just
// normalizes the requested name and does a keyed lookup. Effective provider honors runtime
// ForceProvider (Settings UI) and the LlmOptions.ForceProvider config value.

using System;
using AgenticSdlc.Domain.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgenticSdlc.Modules.Llm;

/// <inheritdoc />
public sealed class LlmClientFactory : ILlmClientFactory
{
    private readonly IServiceProvider _services;
    private readonly LlmOptions _options;
    private readonly IRuntimeOverrides _overrides;

    public LlmClientFactory(IServiceProvider services, IOptions<LlmOptions> options, IRuntimeOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(options);
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options.Value ?? new LlmOptions();
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
    }

    /// <inheritdoc />
    public ILlmClient CreateDefault() => Create(_options.Provider);

    /// <inheritdoc />
    public ILlmClient Create(string providerName)
    {
        var force = !string.IsNullOrWhiteSpace(_overrides.ForceProvider)
            ? _overrides.ForceProvider
            : _options.ForceProvider;
        var effective = string.IsNullOrWhiteSpace(force) ? providerName : force;
        if (string.IsNullOrWhiteSpace(effective))
        {
            throw new ArgumentException("Provider name must not be empty.", nameof(providerName));
        }

        var key = NormalizeKey(effective);
        var client = _services.GetKeyedService<ILlmClient>(key)
            ?? throw new LlmException(
                $"LLM provider '{providerName}' (resolved to '{key}') is not registered. "
                + "Expected: Claude | AzureOpenAI | Mock | MAF | RemoteAgent. "
                + "Check that the corresponding module is loaded.");
        return client;
    }

    private static string NormalizeKey(string providerName) => providerName.Trim().ToUpperInvariant() switch
    {
        "CLAUDE" or "ANTHROPIC" => "Claude",
        "AZUREOPENAI" or "AZURE" or "OPENAI" => "AzureOpenAI",
        "MOCK" or "FAKE" or "STUB" => "Mock",
        "MAF" or "MAF-AZURE" or "AGENTFRAMEWORK" => "MAF",
        "REMOTEAGENT" or "REMOTE" or "IDE" => "RemoteAgent",
        _ => throw new LlmException($"Unknown LLM provider: '{providerName}'. Expected Claude | AzureOpenAI | Mock | MAF | RemoteAgent."),
    };
}
