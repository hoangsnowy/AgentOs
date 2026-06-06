---
name: cost-report
description: >
  Aggregate LLM cost from AgentOs structured logs into a markdown + xlsx report grouped by
  agent / provider / model / date. Use when the user says "cost report", "/cost-report week",
  "weekly cost", "monthly burn", "compare Claude vs Azure spend".
---

Aggregate LLM gateway cost from logs into a table + chart.

## When

- Tracking burn rate weekly / monthly.
- After changing a model alias — measure impact.
- Explicit: "cost report week" / "month".

## Input

1. **Range**: `today` | `week` | `month` | `YYYY-MM-DD..YYYY-MM-DD`.
2. **Group by**: `agent` | `provider` | `model` | `date` | `endpoint` (default `agent + provider`).
3. **Source** (in preference order):
   1. **In-app CSV export** (canonical, DB-backed) — `GET /cost/export?days={N}` on the Web (admin-gated,
      tenant-scoped, no log parsing). The Cost desktop app's "Export CSV" button hits the same endpoint.
      This reads the persisted `pipeline.run_metrics`, so it is exact and needs no regex.
   2. **Structured `LlmCallCompleted` event** — every provider client now logs one per call:
      `LlmCallCompleted {Provider} {Model} {InTok} {OutTok} {CostUsd} {Ms} {Tenant}`. Scrape this from a
      JSON sink / OTel log export when you need raw per-call rows the CSV summary doesn't carry.
   3. Application Insights query in Azure-deployed envs.

> Telemetry note: each LLM call also emits an OpenTelemetry span (`AgentOs.Llm` source) + the
> `agentos.llm.cost.usd` / `gen_ai.client.token.usage` metrics. For dashboards, prefer querying those in
> the OTLP backend over scraping logs.

## Steps

### 1. Pull the data (prefer the CSV export — no parsing)

```bash
# Admin cookie session against the running Web. days=0 → all time.
curl -fsS "https://localhost:5180/cost/export?days=30" -o tools/cost/cost.csv
```
The CSV columns are `section,key,cost_usd,tokens_in,tokens_out,calls` with a `total` row plus
`agent` / `provider` / `model` / `day` breakdown rows. Load it straight into pandas — skip steps 2's regex.

If you need per-call granularity (not just the summary), fall back to scraping the structured
`LlmCallCompleted` event from a JSON log sink. Verify a sink exists in `src/AgentOs.Web/Program.cs` /
`src/AgentOs.Api/Program.cs` (`builder.Logging.AddJsonConsole(...)` or Serilog File → `logs/llm-{date}.jsonl`);
missing → suggest one.

### 2. Parse (only for the per-call fallback)

`tools/cost/parse_logs.py` (commit, reusable) — parses the structured `LlmCallCompleted` event:

```python
import json, glob, pandas as pd

rows = []
for f in glob.glob("logs/llm-*.jsonl"):
    for line in open(f, encoding="utf-8"):
        e = json.loads(line)
        p = e.get("Properties") or e
        if (e.get("MessageTemplate") or e.get("Message", "")).startswith("LlmCallCompleted"):
            rows.append({
                "ts": e.get("@t") or e.get("Timestamp"),
                "provider": p.get("Provider", "?"),
                "model": p.get("Model", "?"),
                "in_tok": int(p.get("InTok", 0)),
                "out_tok": int(p.get("OutTok", 0)),
                "cost_usd": float(p.get("CostUsd", 0)),
                "latency_ms": float(p.get("Ms", 0)),
                "tenant": p.get("Tenant", ""),
            })

pd.DataFrame(rows).to_parquet("tools/cost/raw.parquet")
```

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
- Provider mix: 62% Claude, 38% Azure OpenAI

## By agent
| Agent | Calls | Total ($) | Avg ($) |
|---|---|---|---|
| RequirementAgent | 312 | 1.42 | 0.0046 |
| CodingAgent | 311 | 2.18 | 0.0070 |
| TestingAgent | 311 | 0.84 | 0.0027 |
| QaAgent | 313 | 0.38 | 0.0012 |

## Single-provider comparison
- 100% Claude Sonnet 4: ~$8.20 (+70%)
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
