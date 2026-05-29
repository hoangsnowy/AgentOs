---
name: prompt-tune
description: >
  Tune the system prompt of one AgentOs pipeline agent via batch eval: send N fixture inputs
  through two prompt variants, score outputs (pass-rate, JSON-valid, token diff), and pick a
  winner. Use when the user says "tune prompt for X agent", "optimize prompt",
  "/prompt-tune RequirementAgent", "try a different prompt".
---

A/B-test an agent's system prompt with a fixture eval set.

## When

- An agent's pass-rate looks weak on existing tests.
- After upgrading a model alias (e.g. Sonnet 4 → Sonnet 4.5) — re-tune.
- User wants to compare two prompt drafts.
- Tightening a structured JSON output schema.

## Input

1. **Agent name**: `Requirement` | `Coding` | `Testing` | `Qa` | custom.
2. **Prompt variants**:
   - **A** (current): read `SystemPrompt` const from `src/AgentOs.Modules.Pipeline/Agents/{Name}Agent.cs`.
   - **B** (proposed): user-supplied string, or read from `docs/prompts/{Name}-v2.md`.
3. **Eval set**: path to a JSON array of `{input, expected}` cases. Default `tests/fixtures/eval/{Name}.json`.
4. **Provider**: `Mock` is fast but useless for real comparison — use `Claude`/`AzureOpenAI` for actual tuning.
5. **N runs per case**: default `3` (reduce sampling variance).

## Steps

### 1. Build eval harness

`tools/eval/PromptEval.cs` (ephemeral, do not commit):

```csharp
// 1. Load tests/fixtures/eval/{Name}.json → List<EvalCase>
// 2. For each variant (A, B):
//    For each case × N runs:
//       new LlmRequest(SystemPrompt = variant, ...)
//       client.SendAsync(...)
//       Parse output (try-catch for structured JSON)
//       Score: pass / json_valid / schema_match / tokens / cost / latency
// 3. Aggregate per variant: mean ± std
// 4. Paired t-test if N ≥ 5
```

### 2. Pass rules

| Agent | Pass = |
|---|---|
| Requirement | JSON parses + `entities.Count >= expected.entitiesMin` + `endpoints.Count >= expected.endpointsMin` |
| Coding | Generated C# compiles in a scratch project + `≥ expected.minClasses` classes |
| Testing | xUnit attributes parse + `≥ expected.minTests` `[Fact]`/`[Theory]` methods |
| Qa | `Score ∈ [0, 1]` + `consistency_flag == expected` |

Log every failure reason to CSV for debugging.

### 3. Report

`docs/prompts/{Name}-tune-{date}.md`:

```markdown
# Prompt tune: {Name}Agent — {date}

## Variant A (current)
- Pass rate: 65% (39/60)
- JSON valid: 78%
- Mean cost: $0.0042 / call
- Mean tokens: 1240 → 580

## Variant B (proposed)
- Pass rate: 84% (50/60)  ↑ +19pp
- JSON valid: 95%  ↑ +17pp
- Mean cost: $0.0058 / call  ↑ +38%
- Mean tokens: 1640 → 720  ↑

## Fail breakdown — A
| Case ID | Reason |
|---|---|
| 003 | Missing "endpoints" key |
| 007 | Entity count = 0 |

## Recommendation
Adopt B if +$0.0016/call is acceptable; pass-rate +19pp vs cost +38%.
Decision: ___
```

### 4. Apply the winner

If B accepted:

```bash
# Edit src/AgentOs.Modules.Pipeline/Agents/{Name}Agent.cs — replace SystemPrompt const with B
dotnet test --filter "FullyQualifiedName~{Name}AgentTests"
```

```bash
git add src/AgentOs.Modules.Pipeline/Agents/{Name}Agent.cs docs/prompts/{Name}-tune-*.md
git commit -m "refactor({name}): adopt prompt variant B (+19pp pass-rate)"
```

If B rejected — commit the report only for the audit trail:
```bash
git add docs/prompts/{Name}-tune-*.md
git commit -m "docs(prompts): eval {Name} variant B — rejected (cost ROI insufficient)"
```

### 5. Cleanup

Delete `tools/eval/PromptEval.cs` (ephemeral). Keep the eval fixture (`tests/fixtures/eval/{Name}.json`) — versioned, reused.

## Safety

- **Cost**: 60 cases × 2 variants × N=3 = 360 calls. Pre-estimate with `CostCalculator`. Warn if > $2.
- **Determinism**: force `Temperature = 0` during eval; record `Seed` when the provider supports it.
- **PII**: eval fixtures must contain no PII / secrets.
- **Mock**: never tune against `MockLlmClient` — fixture hits make metrics meaningless. Force a real provider.

## Out of scope

- Auto-generating prompt variants (user's design call).
- Multi-agent tuning at once (one agent per run).
- Production gating on metrics (no CD pipeline for the prototype).
