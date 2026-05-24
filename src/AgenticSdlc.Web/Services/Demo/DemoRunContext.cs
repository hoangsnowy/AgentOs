// AgenticSdlc.Web/Services/Demo/DemoRunContext.cs
// Phase 7 — Per-circuit state that decides which LLM source the current run uses.

namespace AgenticSdlc.Web.Services.Demo;

/// <summary>
/// Context for a single pipeline run on one Blazor circuit. The Studio page sets
/// <see cref="UseDemo"/> before resolving the orchestrator; <see cref="DemoAwareLlmClientFactory"/>
/// reads this flag to pick DemoLlmClient (offline) or the real client.
/// </summary>
public sealed class DemoRunContext
{
    /// <summary>
    /// <c>true</c> ⇒ every agent uses <see cref="DemoLlmClient"/> (canned JSON, runs offline).
    /// <c>false</c> ⇒ uses the real provider per each agent's configuration (Claude / Azure OpenAI).
    /// </summary>
    public bool UseDemo { get; set; } = true;
}
