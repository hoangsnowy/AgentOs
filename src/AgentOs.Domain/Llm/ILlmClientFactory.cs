// Factory for resolving an ILlmClient by provider name. Lives in Domain so agents (Pipeline module)
// can take a dependency on it without referencing the Llm module's implementation.

namespace AgentOs.Domain.Llm;

/// <summary>
/// Factory that selects the <see cref="ILlmClient"/> impl based on <see cref="LlmOptions.Provider"/>.
/// Supported providers (case-insensitive): <c>Claude</c>, <c>AzureOpenAI</c>, <c>MAF</c>,
/// <c>RemoteAgent</c>. Implementations register a keyed <see cref="ILlmClient"/> per provider.
/// </summary>
public interface ILlmClientFactory
{
    /// <summary>Returns the client for the provider configured as default in <see cref="LlmOptions"/>.</summary>
    ILlmClient CreateDefault();

    /// <summary>Returns the client for the specified provider (overrides the default configuration).</summary>
    ILlmClient Create(string providerName);
}
