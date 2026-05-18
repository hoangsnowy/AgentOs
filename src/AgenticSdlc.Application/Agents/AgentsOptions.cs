// AgenticSdlc.Application/Agents/AgentsOptions.cs
// Phase 3 — Options binding cho cấu hình per-agent (section "Agents" trong appsettings).

namespace AgenticSdlc.Application.Agents;

/// <summary>
/// Cấu hình tập hợp 5 agent — bind từ section <c>"Agents"</c>.
/// </summary>
public sealed class AgentsOptions
{
    /// <summary>Section name cho <c>Configuration.GetSection</c>.</summary>
    public const string SectionName = "Agents";

    /// <summary>Cấu hình Orchestrator.</summary>
    public AgentOptions Orchestrator { get; set; } = new() { Model = "claude-haiku-4-5", Temperature = 0.3, MaxTokens = 2000 };

    /// <summary>Cấu hình RequirementAgent.</summary>
    public AgentOptions Requirement { get; set; } = new() { Model = "claude-sonnet-4", Temperature = 0.1, MaxTokens = 2000 };

    /// <summary>Cấu hình CodingAgent.</summary>
    public AgentOptions Coding { get; set; } = new() { Provider = "AzureOpenAI", Model = "gpt-4.1", Temperature = 0.2, MaxTokens = 4000 };

    /// <summary>Cấu hình TestingAgent.</summary>
    public AgentOptions Testing { get; set; } = new() { Provider = "AzureOpenAI", Model = "gpt-4o-mini", Temperature = 0.2, MaxTokens = 3000 };

    /// <summary>Cấu hình QaAgent.</summary>
    public AgentOptions Qa { get; set; } = new() { Model = "claude-haiku-4-5", Temperature = 0.1, MaxTokens = 1500 };
}

/// <summary>Cấu hình 1 agent đơn lẻ.</summary>
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
