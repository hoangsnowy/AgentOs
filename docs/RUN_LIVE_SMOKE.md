# Live LLM Smoke Test

Proves that ClaudeClient + AzureOpenAiClient can call the real API. **Skipped** by default in `dotnet test` so CI needs no secrets + costs nothing.

## When to run

- Before the defense demo — to confirm the 2 real clients still work.
- When bumping the model version or changing the endpoint config.
- NOT in CI by default.

## Estimated cost

~$0.01 per run (2 calls, each ≤ 100 output tokens with a cheap model).

## How to run (PowerShell)

```powershell
# 1. Set env vars
$env:RUN_LIVE_LLM = "1"
$env:ANTHROPIC_API_KEY = "sk-ant-..."
$env:AZURE_OPENAI_API_KEY = "..."
$env:AZURE_OPENAI_ENDPOINT = "https://<resource>.openai.azure.com"

# Optional — override model/deployment
$env:ANTHROPIC_MODEL = "claude-haiku-4-5"           # default
$env:AZURE_OPENAI_DEPLOYMENT = "gpt-4.1"            # default

# 2. Run the smoke test only
dotnet test --filter "FullyQualifiedName~LiveLlmSmokeTests"

# 3. Unset when done
$env:RUN_LIVE_LLM = $null
$env:ANTHROPIC_API_KEY = $null
$env:AZURE_OPENAI_API_KEY = $null
```

## Expected output

```
Passed!  - Failed: 0, Passed: 2, Skipped: 0
```

Each test asserts:
- Response content is non-empty.
- `InputTokens > 0`, `OutputTokens > 0` (the provider returns real usage).
- `Latency < 30s` (HttpClient 30s timeout).
- `Provider` = "Claude" or "AzureOpenAI".

## When skipped

If `RUN_LIVE_LLM != 1` or an API key is missing, the test prints:

```
Skipped: 2 — RUN_LIVE_LLM != 1 or <KEY> not set.
```

## Security

- Do NOT commit API keys.
- Do NOT paste keys into fixtures / logs / commit messages.
- Rotate keys after the defense if leakage is a concern.
- The CI workflow `live-smoke.yml` (not yet created) will use GitHub Secrets, run manually via `workflow_dispatch`.
