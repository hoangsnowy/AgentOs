---
name: cost-report
description: >
  Sinh báo cáo cost từ log `LlmResponse.CostUsd` của agentic-sdlc-net, group by agent /
  provider / model / date. Output .xlsx + markdown summary cho luận văn Chương 4 (so sánh
  hybrid LLM strategy vs single-provider). Use when user says "cost report", "báo cáo chi phí",
  "/cost-report week", "tổng cost tháng này", "so sánh Claude vs Azure". Auto-trigger
  cuối mỗi phase ≥ 2 hoặc trước demo.
---

Aggregate cost LLM gateway từ log → table + chart cho luận văn.

## Khi nào dùng

- Cuối tuần / tháng dev: track burn rate.
- Trước báo cáo luận văn Chương 4 (so sánh hybrid vs all-Claude / all-Azure).
- Sau khi thay model alias để measure impact.
- User explicit: "cost report week" / "tháng 5".

## Input

1. **Range**: `today` | `week` | `month` | `phase-N` | `YYYY-MM-DD..YYYY-MM-DD`.
2. **Group by**: `agent` | `provider` | `model` | `date` | `endpoint` (default `agent + provider`).
3. **Source**: file log (default `logs/llm-{date}.jsonl`) hoặc Application Insights query (nếu deploy Azure).

## Steps

### 1. Đảm bảo log có cost

`{Name}Agent.RunAsync` (theo `agent-scaffold` skill) phải log structured:

```csharp
_logger.LogInformation(
    "{Agent} done: {InTok}→{OutTok} tokens, ${Cost}, {Ms}ms",
    nameof(XxxAgent), response.InputTokens, response.OutputTokens, response.CostUsd, response.Latency.TotalMilliseconds);
```

Cần JSON file sink. Verify `src/AgenticSdlc.Api/Program.cs` có:

```csharp
builder.Logging.AddJsonConsole(opts => opts.IncludeScopes = true);
// hoặc Serilog with File sink to logs/llm-{date}.jsonl
```

Nếu chưa có → first run skill bảo user setup, gợi ý Serilog.Sinks.File.

### 2. Parse log

Script Python `tools/cost/parse_logs.py` (commit, reusable):

```python
import json, glob, re, pandas as pd, sys

rows = []
for f in glob.glob("logs/llm-*.jsonl"):
    for line in open(f, encoding="utf-8"):
        e = json.loads(line)
        msg = e.get("Message") or e.get("message", "")
        m = re.match(r"(\w+Agent) done: (\d+)→(\d+) tokens, \$([0-9.]+), ([0-9.]+)ms", msg)
        if not m:
            continue
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

Cleaner: ghi structured event `LlmCallCompleted` thay vì regex parse message. Refactor Application logger nếu user OK.

### 3. Aggregate + report

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

# Output .xlsx (dùng anthropic-skills:xlsx hoặc openpyxl)
with pd.ExcelWriter("docs/cost/cost-report-{date}.xlsx") as w:
    summary.to_excel(w, sheet_name="By agent")
    df.groupby("date")["cost_usd"].sum().to_excel(w, sheet_name="Daily total")
    df.groupby("provider")["cost_usd"].sum().to_excel(w, sheet_name="By provider")
```

### 4. Markdown summary cho luận văn

`docs/cost/cost-{range}.md`:

```markdown
# Cost report — {range}

## Tổng
- Total calls: 1,247
- Total cost: $4.82
- Avg cost / call: $0.0039
- Hybrid distribution: 62% Claude, 38% Azure OpenAI

## By agent
| Agent | Calls | Total ($) | Avg ($) |
|---|---|---|---|
| RequirementAgent | 312 | 1.42 | 0.0046 |
| CodingAgent | 311 | 2.18 | 0.0070 |
| TestingAgent | 311 | 0.84 | 0.0027 |
| QaAgent | 313 | 0.38 | 0.0012 |

## So sánh giả định all-Claude / all-Azure

Nếu chạy 100% Claude Sonnet 4: ~$8.20 (+70%)
Nếu chạy 100% GPT-4.1: ~$5.95 (+23%)
Hybrid hiện tại: $4.82 — saving 41% / 19% vs single-provider.

→ Validates KC4 Mục 2.5: hybrid LLM cost-efficient.

## Trend
{chart placeholder — generate qua matplotlib hoặc Excel chart}
```

### 5. Commit

```bash
git add docs/cost/cost-report-*.xlsx docs/cost/cost-*.md tools/cost/parse_logs.py
git commit -m "docs(cost): cost report {range} — hybrid saves N% vs single-provider"
```

## Safety / quality

- **Không commit raw log** (`logs/*.jsonl` phải trong `.gitignore`) — chứa request/response có thể PII.
- **Verify cost vs CostCalculator**: aggregate cost từ log phải khớp `inputTokens × inputPrice + outputTokens × outputPrice` ± 1%. Drift lớn → pricing table outdated.
- **Time zone**: log timestamp ISO UTC; report convert sang Asia/Ho_Chi_Minh khi present user.
- **Privacy**: nếu log có prompt content, sanitize trước khi share report (regex strip API key pattern, email, etc).

## Reusable across phases

Script `tools/cost/parse_logs.py` commit 1 lần, dùng tiếp các phase sau. Skill chỉ wire-up + chạy + format report. Đổi pricing snapshot (Q3/2026, Q4/2026) → update `CostCalculator.cs` chính, không sửa skill này.

## Out of scope

- Real-time cost alert (cần webhook / Azure Monitor — không phải prototype scope).
- Cost optimization recommendation tự động (đó là `prompt-tune` skill domain).
- Billing reconciliation với Anthropic / Azure invoice (manual cho luận văn).
