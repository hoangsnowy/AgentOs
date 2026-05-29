// AgenticSdlc.Domain/Llm/LlmOptions.cs
// Sprint 1 — Options binding for the LLM Gateway (bound from the appsettings "Llm" section).

namespace AgenticSdlc.Domain.Llm;

/// <summary>
/// Root options bound from the <c>appsettings.json</c> <c>"Llm"</c> section.
/// </summary>
public sealed class LlmOptions
{
    /// <summary>Section name used for <c>Configuration.GetSection(...)</c>.</summary>
    public const string SectionName = "Llm";

    /// <summary>Default provider: <c>"Claude"</c>, <c>"AzureOpenAI"</c>, or <c>"Mock"</c>.</summary>
    public string Provider { get; set; } = "Mock";

    /// <summary>
    /// Optional. When set (e.g. <c>"AzureOpenAI"</c>), every agent uses this provider regardless of its
    /// per-agent <c>Agents:&lt;Name&gt;:Provider</c>. Lets you run the whole pipeline on one provider
    /// (e.g. Azure-only, no Anthropic key needed). Leave empty/null for the per-agent hybrid configuration.
    /// </summary>
    public string? ForceProvider { get; set; }

    /// <summary>Claude (Anthropic) client configuration.</summary>
    public ClaudeOptions Claude { get; set; } = new();

    /// <summary>Azure OpenAI client configuration.</summary>
    public AzureOpenAiOptions AzureOpenAi { get; set; } = new();

    /// <summary>Mock client configuration (fixture-based, used for offline testing).</summary>
    public MockOptions Mock { get; set; } = new();
}

/// <summary>Options for Claude (Anthropic Messages API).</summary>
public sealed class ClaudeOptions
{
    /// <summary>API key (set via user-secrets or env var, do NOT commit).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional pool of API keys for round-robin + rate-limit failover. When a key returns HTTP 429 the
    /// router cools it down and routes to the next key. Combined with <see cref="ApiKey"/> (and any runtime
    /// override) into one distinct pool. Example: <c>"Llm:Claude:ApiKeys": ["sk-ant-a","sk-ant-b"]</c>.
    /// </summary>
    public System.Collections.Generic.List<string> ApiKeys { get; set; } = new();

    /// <summary>Base URL of the Anthropic API.</summary>
    public string Endpoint { get; set; } = "https://api.anthropic.com";

    /// <summary>Default model (for example <c>claude-sonnet-4-20250514</c>).</summary>
    public string Model { get; set; } = "claude-sonnet-4-20250514";

    /// <summary>The <c>anthropic-version</c> header.</summary>
    public string ApiVersion { get; set; } = "2023-06-01";

    /// <summary>Maximum number of retries on 429/5xx/timeout.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>HTTP timeout per request (seconds).</summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>Options for Azure OpenAI.</summary>
public sealed class AzureOpenAiOptions
{
    /// <summary>Azure OpenAI API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional pool of API keys for round-robin + rate-limit failover (e.g. keys from several Azure
    /// resources/regions). On HTTP 429 a key is cooled down and traffic routes to the next.
    /// </summary>
    public System.Collections.Generic.List<string> ApiKeys { get; set; } = new();

    /// <summary>Azure OpenAI endpoint (for example <c>https://my-resource.openai.azure.com</c>).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Deployment name / model alias.</summary>
    public string Model { get; set; } = "gpt-4.1";

    /// <summary>API version (for example <c>2024-10-21</c>).</summary>
    public string ApiVersion { get; set; } = "2024-10-21";

    /// <summary>Maximum number of retries.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Timeout (seconds).</summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>Options for the Mock client.</summary>
public sealed class MockOptions
{
    /// <summary>Path (relative or absolute) to the folder containing the JSON fixtures.</summary>
    public string FixturePath { get; set; } = "tests/fixtures/llm";

    /// <summary>Simulated latency (ms) when returning a fixture, so downstream code does not assume zero latency.</summary>
    public int SimulatedLatencyMs { get; set; } = 5;
}
