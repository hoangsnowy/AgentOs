# Live LLM Pipeline Smoke

End-to-end pipeline (Requirement → Coding → Testing → QA) gọi LLM thật, 1 vòng, budget guard ≤ $0.50.

## Khi nào chạy

- Trước demo bảo vệ — confirm full pipeline thực chạy được, không chỉ unit-level smoke (xem `RUN_LIVE_SMOKE.md`).
- Sau khi bump model version / sửa prompt v1.
- KHÔNG chạy CI default.

## Cost guard

- `MaxBudgetUsd = 0.50` (assert nếu vượt).
- `MaxCallCount = 5` (1 per agent + 1 buffer).
- `Pipeline:MaxIterations = 1` (không QA loop retry).
- Test fail nếu metric vượt — fail-safe budget.

## Cách chạy (PowerShell)

```powershell
$env:RUN_LIVE_LLM = "1"
$env:ANTHROPIC_API_KEY = "sk-ant-..."
# Optional: $env:ANTHROPIC_MODEL = "claude-haiku-4-5"   # default
# Hoặc Azure:
$env:AZURE_OPENAI_API_KEY = "..."
$env:AZURE_OPENAI_ENDPOINT = "https://<resource>.openai.azure.com"
# Optional: $env:AZURE_OPENAI_DEPLOYMENT = "gpt-4.1"

dotnet test --filter "FullyQualifiedName~LivePipelineSmokeTests"

$env:RUN_LIVE_LLM = $null
$env:ANTHROPIC_API_KEY = $null
```

## Output

`TestResults/live_pipeline_smoke_claude.json` hoặc `live_pipeline_smoke_azure.json`:

```json
{
  "timestamp": "2026-05-18T...",
  "provider": "Claude",
  "userStory": "Hệ thống TODO list...",
  "nMax": 1,
  "resultStatus": "Done",
  "iterationCount": 1,
  "totalCostUsd": 0.012,
  "callCount": 4,
  "metrics": [ { agent, tokens, latency, cost, ... }, ... ],
  "specTitle": "Quản lý TODO",
  "codeFileCount": 5,
  "testFileCount": 2,
  "qaScore": 0.85
}
```

Transcript được ghi **kể cả khi assert fail** — debug-friendly.

## Khi skip

```
Skipped: RUN_LIVE_LLM != 1
hoặc
Skipped: ANTHROPIC_API_KEY not set / AZURE_OPENAI_API_KEY / AZURE_OPENAI_ENDPOINT not set
```

## Bảo mật

- KHÔNG commit key.
- Transcript JSON KHÔNG chứa secret (chỉ chứa response content + metric).
- Inspect trước khi commit: `cat TestResults/live_pipeline_smoke_*.json`.
- Rotate key sau bảo vệ.
