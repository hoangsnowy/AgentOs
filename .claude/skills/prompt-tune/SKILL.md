---
name: prompt-tune
description: >
  Tune system prompt của 1 agent trong agentic-sdlc-net. Batch eval: gửi N fixture
  input → so output thực tế vs expected → report drift (pass-rate, JSON-valid, token diff).
  Hỗ trợ A/B 2 prompt variant. Use when user says "tune prompt for X agent", "tối ưu prompt",
  "/prompt-tune RequirementAgent", "agent X đang fail nhiều case", "đổi prompt thử".
  Auto-trigger khi KC bench cho 1 agent < 70% pass.
---

Eval + iterate system prompt của 1 agent: batch chạy fixture → so output → A/B test.

## Khi nào dùng

- Agent có pass-rate thấp trong KC bench (< 70%).
- Sau khi đổi model alias (vd Sonnet 4 → Sonnet 4.5) cần re-tune.
- User muốn so 2 phiên bản prompt (current vs proposal).
- Tinh chỉnh JSON output schema (Coding / Requirement agent).

## Input

1. **Agent tên**: `Requirement` | `Coding` | `Testing` | `QA` | custom.
2. **Prompt variant**:
   - **A** (current): đọc từ source `src/AgenticSdlc.Infrastructure/Agents/{Name}Agent.cs` const `SystemPrompt`.
   - **B** (proposed): user provide qua message, hoặc đọc từ file `docs/prompts/{Name}-v2.md`.
3. **Eval set**: path JSON array các case `{input, expected}`. Default `tests/fixtures/eval/{Name}.json`.
4. **Provider**: thường giữ `Mock` cho speed → nhưng tune thật phải `Anthropic`/`AzureOpenAI` (real LLM).
5. **N runs / case**: default 3 (giảm variance random sampling).

## Steps

### 1. Build eval harness

File `tools/eval/PromptEval.cs` (ephemeral, không commit):

```csharp
// Cấu trúc:
// 1. Đọc tests/fixtures/eval/{Name}.json → List<EvalCase>
// 2. For mỗi prompt variant (A, B):
//    For mỗi case × N runs:
//       Build LlmRequest với SystemPrompt = variant
//       Gọi ILlmClient.SendAsync
//       Parse output (try-catch JSON nếu structured)
//       Compute metric:
//          - pass: output match expected (rule per agent)
//          - json_valid: deserialize OK
//          - schema_match: required fields present
//          - tokens_in, tokens_out, cost_usd, latency_ms
// 3. Aggregate per variant: mean ± std
// 4. Statistical test (paired t-test nếu N ≥ 5)
```

### 2. Pass rule per agent

| Agent | Pass = |
|---|---|
| Requirement | Parse JSON OK + ≥ `expected.entitiesMin` entity + ≥ `expected.endpointsMin` endpoint |
| Coding | C# code compile (chạy `dotnet build` trên scratch project) + ≥ `expected.minClasses` class |
| Testing | xUnit attribute valid + ≥ `expected.minTests` test method |
| QA | Score ∈ [0, 1] + consistency_flag matches expected boolean |

Detect malformed bằng try-catch `JsonSerializer.Deserialize` + custom validator per agent. Log lý do fail vào CSV (cho debug).

### 3. Report

Output `docs/prompts/{Name}-tune-{date}.md`:

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

## Fail breakdown — Variant A
| Case ID | Reason |
|---|---|
| KC1-003 | Missing "endpoints" key |
| KC1-007 | Entity count = 0 |
...

## Recommendation
Adopt B nếu cost increase $+0.0016/call chấp nhận được. ROI: pass-rate +19pp vs cost +38%.
Decision: ___ (user fill)
```

### 4. Apply variant đã chọn

Nếu user accept B:

```bash
# Edit src/AgenticSdlc.Infrastructure/Agents/{Name}Agent.cs
# Replace const SystemPrompt = """...""" với variant B
dotnet test --filter "FullyQualifiedName~{Name}AgentTests"
```

Commit:
```bash
git add src/AgenticSdlc.Infrastructure/Agents/{Name}Agent.cs docs/prompts/{Name}-tune-*.md
git commit -m "refactor({name}): adopt prompt variant B (+19pp pass-rate)"
```

Nếu reject B: chỉ commit report (cho audit trail luận văn):
```bash
git add docs/prompts/{Name}-tune-*.md
git commit -m "docs(prompts): eval {Name} variant B — rejected (cost ROI insufficient)"
```

### 5. Cleanup

Xoá `tools/eval/PromptEval.cs` (ephemeral). Giữ lại eval fixture (`tests/fixtures/eval/{Name}.json`) — versioned, dùng lại lần sau.

## Safety

- **Cost warning**: 60 case × 2 variant × N=3 = 360 call. Pre-estimate qua `CostCalculator`. Báo user nếu > $2.
- **Deterministic**: ép `Temperature=0` khi eval (variance khác sẽ làm metric noisy). Lưu seed nếu provider support.
- **PII**: eval fixture KHÔNG được chứa PII / secret. Sanitize input trước commit.
- **Cache fixture**: nếu Mock provider có hit, KHÔNG dùng eval — phải force real provider (tune cần real output).

## Out of scope

- Generate prompt variant tự động (đó là design task user phải làm).
- Cross-agent tune (vd Orchestrator + QA cùng lúc) — skill này 1 agent / run.
- Production deploy gated on metric — không có CD pipeline cho prototype.
