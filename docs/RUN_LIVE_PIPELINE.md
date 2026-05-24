# Live LLM Pipeline Smoke

End-to-end pipeline (Requirement → Coding → Testing → QA) calling a real LLM, 1 iteration, budget guard ≤ $0.50.

## When to run

- Before the defense demo — to confirm the full pipeline actually runs, not just the unit-level smoke (see `RUN_LIVE_SMOKE.md`).
- After bumping the model version / editing the v1 prompts.
- NOT in CI by default.

## Cost guard

- `MaxBudgetUsd = 0.50` (asserts if exceeded).
- `MaxCallCount = 5` (1 per agent + 1 buffer).
- `Pipeline:MaxIterations = 1` (no QA loop retry).
- The test fails if a metric is exceeded — a fail-safe budget.

## How to run (PowerShell)

```powershell
$env:RUN_LIVE_LLM = "1"
$env:ANTHROPIC_API_KEY = "sk-ant-..."
# Optional: $env:ANTHROPIC_MODEL = "claude-haiku-4-5"   # default
# Or Azure:
$env:AZURE_OPENAI_API_KEY = "..."
$env:AZURE_OPENAI_ENDPOINT = "https://<resource>.openai.azure.com"
# Optional: $env:AZURE_OPENAI_DEPLOYMENT = "gpt-4.1"

dotnet test --filter "FullyQualifiedName~LivePipelineSmokeTests"

$env:RUN_LIVE_LLM = $null
$env:ANTHROPIC_API_KEY = $null
```

## Output

`TestResults/live_pipeline_smoke_claude.json` or `live_pipeline_smoke_azure.json`:

```json
{
  "timestamp": "2026-05-18T...",
  "provider": "Claude",
  "userStory": "TODO list system...",
  "nMax": 1,
  "resultStatus": "Done",
  "iterationCount": 1,
  "totalCostUsd": 0.012,
  "callCount": 4,
  "metrics": [ { agent, tokens, latency, cost, ... }, ... ],
  "specTitle": "TODO Management",
  "codeFileCount": 5,
  "testFileCount": 2,
  "qaScore": 0.85
}
```

The transcript is written **even when an assert fails** — debug-friendly.

## When skipped

```
Skipped: RUN_LIVE_LLM != 1
or
Skipped: ANTHROPIC_API_KEY not set / AZURE_OPENAI_API_KEY / AZURE_OPENAI_ENDPOINT not set
```

## Security

- Do NOT commit keys.
- The JSON transcript does NOT contain secrets (only response content + metrics).
- Inspect before committing: `cat TestResults/live_pipeline_smoke_*.json`.
- Rotate keys after the defense.
