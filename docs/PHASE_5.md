# Phase 5 — Unit tests + benchmark KC1-KC5

> Status: ✅ Done — 2026-05-18

## Objectives

Complete the test coverage for the 5 agents + orchestrator, add the KC1-KC5 dataset (thesis Section 2.5) and a bench harness to run experiments offline (Mock provider) or online (Claude / Azure OpenAI).

## Deliverables

### Tests

- `tests/AgenticSdlc.Tests/Pipeline/SequencedLlmClient.cs` — a test `ILlmClient` that returns responses in call order (in-memory queue, captures requests for assertions).
- `tests/AgenticSdlc.Tests/Pipeline/PipelineEndToEndTests.cs` — 3 E2E cases:
  - `Pipeline_QaPassFirstIteration_AggregatesAllMetrics` — 4 canned responses, passes on iteration 1, verifies aggregated metrics.
  - `Pipeline_QaFailsThenPasses_TwoIterations` — 7 canned responses, QA fails on iteration 1 → passes on iteration 2.
  - `Pipeline_MalformedRequirementResponse_ReturnsFailed` — verifies the failure mode.

Total test count: **80** (+3 from Phase 4).

### KC dataset

`tests/fixtures/kc/kc{1-5}.json` — each file is an array of `{id, input, expected}`. The README describes the convention + how to add a case.

### Bench harness

The `/kc-bench` skill (shipped in Phase 2-skills) is the entrypoint for running the bench. The skill automatically:
1. Starts the API locally if needed (`dotnet run --project src/AgenticSdlc.Api`).
2. Loops over the dataset × N iterations (default 3).
3. POSTs requests, capturing metrics from the response body.
4. Aggregates → `.xlsx` + `.md` report using Anthropic's `xlsx` skill.

The bench does NOT implement a standalone tool — it leverages the existing skill stack to avoid duplication.

## Technical decisions

- **Why SequencedLlmClient instead of MockLlmClient (hash-based)?**
  The hash in `MockLlmClient.ComputeHash` depends on the exact prompt content. The CodingAgent prompt includes the JSON-serialized `RequirementSpec` → if the RequirementAgent output changes (even whitespace), the CodingAgent hash changes → brittle fixtures. The sequenced client trades off determinism (canned per call) for stability in E2E tests.

- **Why is the KC5 `PassScore = 0.8` threshold not higher?**
  Thesis Section 2.4.5 — pragmatic enough for the prototype, avoiding many QA-loop iterations with the Sonnet 4 LLM (cost). It can be raised to 0.85 if the KC4 pass-rate > 70%.

- **Why do KC2/3/5 use a `specFixture` reference?**
  To split the dataset into a DAG (KC1 → KC2 → KC3 → KC5; KC4 runs independently). This allows replacing one fixture without re-generating the whole chain. When a fixture is missing, the bench skill skips the case + reports it.

## Thesis references

- Section 2.5 — Experimental scenarios KC1-KC5
- Section 3.3 — Measurement methodology (token, cost, latency, pass-rate)
- Section 4.2 — Benchmark results report (to be filled in after running the real bench)

## Next phase

→ Phase 6 — Azure deployment (Container Apps + App Insights). Build Docker image, Bicep IaC, GitHub Actions deploy workflow.
