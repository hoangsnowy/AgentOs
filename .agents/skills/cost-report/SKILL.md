---
name: cost-report
description: >
  Aggregate LLM cost from AgentOs structured logs into a markdown + xlsx report grouped by
  agent / provider / model / date. Use when the user says "cost report", "/cost-report week",
  "weekly cost", "monthly burn", "compare Codex vs Azure spend".
---

Aggregate LLM gateway cost from logs into a table + chart.

## When

- Tracking burn rate weekly / monthly.
- After changing a model alias — measure impact.
- Explicit: "cost report week" / "month".

## Input

1. **Range**: `today` | `week` | `month` | `YYYY-MM-DD..YYYY-MM-DD`.
2. **Group by**: `agent` | `provider` | `model` | `date` | `endpoint` (default `agent + provider`).
3. **Source**: file log (`logs/llm-{date}.jsonl`) or Application Insights query in Azure-deployed envs.

## Steps

### 1. Verify log has cost

Pipeline agents (per `agent-scaffold` skill) log:
```csharp
_logger.LogInformation(
    "{Agent} done: {InTok}->{OutTok} tokens, ${Cost}, {Ms}ms",
    nameof(XxxAgent), response.InputTokens, response.OutputTokens, response.CostUsd, response.Latency.TotalMilliseconds);
```

Need a JSON sink. Verify `src/AgentOs.Api/Program.cs`:
```csharp
builder.Logging.AddJsonConsole(opts => opts.IncludeScopes = true);
// or Serilog with File sink → logs/llm-{date}.jsonl
```

Missing → suggest Serilog.Sinks.File.

### 2. Parse log

`tools/cost/parse_logs.py` (commit, reusable):

```python
import json, glob, re, pandas as pd

rows = []
for f in glob.glob("logs/llm-*.jsonl"):
    for line in open(f, encoding="utf-8"):
        e = json.loads(line)
        msg = e.get("Message") or e.get("message", "")
        m = re.match(r"(\w+Agent) done: (\d+)->(\d+) tokens, \$([0-9.]+), ([0-9.]+)ms", msg)
        if not m: continue
        rows.append({
            "ts": e["@t"],
            "agent": m.group(1),
            "in_tok": int(m.group(2)),
            "out_tok": int(m.group(3)),
            "cost_usd": float(m.group(4)),
            "latency_ms": float(m.group(5)),
            "provider": e.get("Properties", {}).get("Provider", "?"),
            "model": e.get("Properties", {}).get("Model", "?"),
        })

pd.DataFrame(rows).to_parquet("tools/cost/raw.parquet")
```

Cleaner long-term: emit a structured `LlmCallCompleted` event instead of regex-parsing the message.

### 3. Aggregate + xlsx

```python
import pandas as pd
df = pd.read_parquet("tools/cost/raw.parquet")
df["date"] = pd.to_datetime(df["ts"]).dt.date

summary = df.groupby(["agent", "provider", "model"]).agg(
    calls=("cost_usd", "size"),
    total_cost=("cost_usd", "sum"),
    avg_cost=("cost_usd", "mean"),
    total_in_tok=("in_tok", "sum"),
    total_out_tok=("out_tok", "sum"),
    avg_latency_ms=("latency_ms", "mean"),
).round(4)

with pd.ExcelWriter(f"docs/cost/cost-report-{range}.xlsx") as w:
    summary.to_excel(w, sheet_name="By agent")
    df.groupby("date")["cost_usd"].sum().to_excel(w, sheet_name="Daily total")
    df.groupby("provider")["cost_usd"].sum().to_excel(w, sheet_name="By provider")
```

### 4. Markdown summary

`docs/cost/cost-{range}.md`:

```markdown
# Cost report — {range}

## Totals
- Total calls: 1,247
- Total cost: $4.82
- Avg cost / call: $0.0039
- Provider mix: 62% Codex, 38% Azure OpenAI

## By agent
| Agent | Calls | Total ($) | Avg ($) |
|---|---|---|---|
| RequirementAgent | 312 | 1.42 | 0.0046 |
| CodingAgent | 311 | 2.18 | 0.0070 |
| TestingAgent | 311 | 0.84 | 0.0027 |
| QaAgent | 313 | 0.38 | 0.0012 |

## Single-provider comparison
- 100% Codex Sonnet 4: ~$8.20 (+70%)
- 100% GPT-4.1: ~$5.95 (+23%)
- Hybrid actual: $4.82 → save 41% / 19%.
```

### 5. Commit

```bash
git add docs/cost/cost-report-*.xlsx docs/cost/cost-*.md tools/cost/parse_logs.py
git commit -m "docs(cost): cost report {range}"
```

## Safety

- Never commit raw logs (`logs/*.jsonl` must be `.gitignore`d) — may contain request bodies / PII.
- Verify aggregated cost ≈ `inputTokens × inputPrice + outputTokens × outputPrice` ± 1%. Drift → pricing table outdated.
- Sanitize prompt content (API key pattern, email) before sharing externally.

## Out of scope

- Real-time alerting (use Azure Monitor / external).
- Auto prompt optimization (`prompt-tune` skill).
- Billing reconciliation with Anthropic / Azure invoices (manual).
