// AgenticSdlc.Application/Agents/AgentsOptions.cs
// Phase 3 — Options binding for per-agent configuration (the "Agents" section in appsettings).

namespace AgenticSdlc.Modules.Pipeline.Agents;

/// <summary>
/// Configuration for the set of 5 agents — bound from the <c>"Agents"</c> section.
/// </summary>
public sealed class AgentsOptions
{
    /// <summary>Section name for <c>Configuration.GetSection</c>.</summary>
    public const string SectionName = "Agents";

    /// <summary>Orchestrator configuration.</summary>
    public AgentOptions Orchestrator { get; set; } = new() { Model = "claude-haiku-4-5", Temperature = 0.3, MaxTokens = 2000 };

    /// <summary>RequirementAgent configuration.</summary>
    public AgentOptions Requirement { get; set; } = new() { Model = "claude-sonnet-4", Temperature = 0.1, MaxTokens = 2000 };

    /// <summary>CodingAgent configuration.</summary>
    public AgentOptions Coding { get; set; } = new() { Provider = "AzureOpenAI", Model = "gpt-4.1", Temperature = 0.2, MaxTokens = 4000 };

    /// <summary>TestingAgent configuration.</summary>
    public AgentOptions Testing { get; set; } = new() { Provider = "AzureOpenAI", Model = "gpt-4o-mini", Temperature = 0.2, MaxTokens = 3000 };

    /// <summary>QaAgent configuration.</summary>
    public AgentOptions Qa { get; set; } = new() { Model = "claude-haiku-4-5", Temperature = 0.1, MaxTokens = 1500 };
}

/// <summary>Configuration for a single agent.</summary>
public sealed class AgentOptions
{
    /// <summary>Provider name: <c>Anthropic</c> / <c>AzureOpenAI</c> / <c>Mock</c>.</summary>
    public string Provider { get; set; } = "Anthropic";

    /// <summary>Model alias.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Sampling temperature [0, 2].</summary>
    public double Temperature { get; set; }

    /// <summary>Output max tokens.</summary>
    public int MaxTokens { get; set; } = 2000;
}
