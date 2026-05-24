# OrchestratorPrompt — v1

Version: **v1** · Source: `src/AgenticSdlc.Application/Prompts/OrchestratorPrompt.cs`

The Orchestrator is currently a deterministic state machine (does NOT call an LLM). This file only records the policy text used for documentation + as a fallback if there is later an LLM-driven "meta-orchestrator".

## Policy

```text
Orchestrator policy (deterministic, does not call an LLM):

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
```

## Changelog
- **v1** (2026-05-18): created the file to snapshot the policy.
