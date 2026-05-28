// AgenticSdlc.Application/Prompts/OrchestratorPrompt.cs
// Sprint 3 — The orchestrator does NOT call the LLM directly (deterministic state machine).
// This file holds policy text used for documentation + in case we later want an LLM "meta-orchestrator".

namespace AgenticSdlc.Application.Prompts;

/// <summary>Orchestrator meta-policy (does not call the LLM currently).</summary>
public static class OrchestratorPrompt
{
    /// <summary>Prompt version.</summary>
    public const string Version = "v1";

    /// <summary>Policy describing the end-to-end pipeline flow.</summary>
    public const string Policy = """
        Orchestrator policy (deterministic, does not call the LLM):

        1. RequirementAgent(story) → spec.
        2. Loop i = 1..NMax:
           a. CodingAgent(spec, qaFeedback[i-1]?) → code.
           b. TestingAgent(spec, code, qaFeedback[i-1]?) → tests.
           c. QaAgent(spec, code, tests) → qaReport.
           d. If qaReport.isConsistent → return PipelineResult(Done, iterations=i).
           e. If i = NMax → return PipelineResult(MaxIterationReached).
        3. If any agent throws LlmException → return PipelineResult(Failed) with the agent + error.

        Any other exception must propagate (do not swallow).
        Metric aggregate: sum of InputTokens, OutputTokens, CostUsd, Latency across all agent calls.
        """;
}
