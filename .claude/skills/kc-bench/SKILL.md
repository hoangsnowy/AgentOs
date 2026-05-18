---
name: kc-bench
description: >
  Chạy benchmark KC1-KC5 (kịch bản thực nghiệm luận văn, Mục 2.5) của agentic-sdlc-net.
  Gọi endpoint /pipeline hoặc agent riêng lẻ với dataset cố định, đo token/cost/latency/pass-rate,
  xuất .xlsx report cho luận văn Chương 3. Use when user says "run KC bench", "benchmark KC4",
  "/kc-bench KC1", "chạy KC1-KC5", "đo metric luận văn". Auto-trigger trước phase 5 commit.
---

Chạy kịch bản KC1-KC5 (Mục 2.5 luận văn) end-to-end, đo metric, xuất `.xlsx` report.

## Kịch bản (theo Mục 2.5)

| KC | Tên | Endpoint | Mục tiêu đo |
|---|---|---|---|
| **KC1** | Requirement analysis | `POST /requirement` | Token, cost, structured output validity |
| **KC2** | Code generation | `POST /code` | Token, compile-pass rate, LoC |
| **KC3** | Test generation | `POST /test` | Token, xUnit-valid rate, coverage estimate |
| **KC4** | Pipeline end-to-end | `POST /pipeline` | Tổng token, tổng cost, iteration count, pass-rate cuối |
| **KC5** | QA consistency loop | `POST /qa` | Drift score, iteration đến hội tụ |

## Khi nào dùng

- Phase 5 (benchmark) hoặc trước buổi báo cáo luận văn.
- User explicit: "chạy KC4", "/kc-bench KC1-KC5".
- So sánh trước/sau tune prompt (kết hợp `prompt-tune` skill).

## Input cần hỏi

1. **KC nào**: 1-5, comma-separated (`KC1,KC4`) hoặc `all`.
2. **Provider**: `Anthropic` | `AzureOpenAI` | `Mock` (default Mock cho dry-run; thật cho luận văn run).
3. **Số lần lặp** mỗi case (default 3, để tính mean ± std).
4. **Dataset path**: default `tests/fixtures/kc/{kcN}.json` (mảng input).

## Steps

### 1. Đảm bảo dataset tồn tại

```bash
ls tests/fixtures/kc/
```

Nếu thiếu file, sinh skeleton:

```json
[
  {
    "id": "KC1-001",
    "input": { "userStory": "Hệ thống cần API quản lý sản phẩm..." },
    "expected": { "entitiesMin": 1, "endpointsMin": 4 }
  }
]
```

Mỗi KC có schema input/expected riêng — đọc luận văn Mục 2.5 để fill chính xác.

### 2. Start API local

```bash
dotnet run --project src/AgenticSdlc.Api &
# Đợi health pass:
until curl -sf http://localhost:5080/health > /dev/null; do sleep 1; done
```

### 3. Run benchmark script

Skill này write 1 script C# / shell tạm — `tools/bench/RunKc.cs` (không commit, ephemeral):

```csharp
// Đọc tests/fixtures/kc/{kcN}.json
// Loop từng case × N lần lặp
// POST tới endpoint tương ứng
// Đo: HttpClient stopwatch, deserialize response, extract token + cost từ field LlmResponse-like
// Aggregate per-case: mean, std, p95
// Output: tools/bench/results/kc{N}-{timestamp}.csv
```

Hoặc gọn hơn: dùng `xunit` test với attribute `[Trait("Category", "Bench")]` skip default, manual trigger:

```bash
dotnet test --filter "Category=Bench" --logger "console;verbosity=detailed"
```

### 4. Aggregate sang .xlsx

Dùng `xlsx` skill (anthropic-skills) hoặc Python `openpyxl`:

```python
# tools/bench/aggregate.py
import pandas as pd, glob, datetime
dfs = [pd.read_csv(f) for f in glob.glob("tools/bench/results/kc*.csv")]
df = pd.concat(dfs)
summary = df.groupby(["kc", "case_id"]).agg(
    n=("latency_ms", "size"),
    cost_mean=("cost_usd", "mean"),
    cost_std=("cost_usd", "std"),
    tok_in_mean=("input_tokens", "mean"),
    tok_out_mean=("output_tokens", "mean"),
    latency_p95=("latency_ms", lambda x: x.quantile(0.95)),
    pass_rate=("pass", "mean"),
)
out = f"docs/bench/kc-summary-{datetime.date.today()}.xlsx"
summary.to_excel(out)
```

Hoặc invoke `anthropic-skills:xlsx` skill trực tiếp với data tổng hợp.

### 5. Output paths

- Raw CSV: `tools/bench/results/kc{N}-{ISO-timestamp}.csv` (gitignore, ephemeral).
- Summary XLSX: `docs/bench/kc-summary-{date}.xlsx` (commit, dùng cho luận văn).
- Markdown report: `docs/bench/kc-{date}.md` (auto-sinh, paste vào Chương 3).

### 6. Commit (chỉ summary, không raw)

```bash
git add docs/bench/kc-summary-*.xlsx docs/bench/kc-*.md
git commit -m "bench: KC1-KC5 results {date} ({provider})"
```

## Safety / cost warning

KC4 (pipeline) tốn nhất — mỗi run ≈ 4 agent × ~2k token in + 2k token out × N iteration. Pre-estimate trước khi chạy:

```
estimated_cost = sum(per_kc_cost × iterations)
```

Nếu > $5 → confirm user trước khi gọi. Cho luận văn (3 lặp × all KC × Claude+OpenAI mix) thường $2-10 / run.

KHÔNG để bench loop chạy nền — fail mid-way mất nhiều token. Bench failure logging vào `tools/bench/results/errors-{timestamp}.log`.

## Verification

```bash
ls docs/bench/kc-summary-*.xlsx
xdg-open docs/bench/kc-summary-*.xlsx  # hoặc start trên Windows
```

Sanity check: cost mean per KC vs `CostCalculator` expected pricing. Drift > 10% → có thể model alias đã đổi, re-record fixture.

## Out of scope

- Implement endpoint `/pipeline` / `/qa` — Phase 4 lo. Skill này assume đã có.
- Tune prompt sau khi thấy pass-rate thấp — dùng skill `prompt-tune`.
- Cost report độc lập (không liên quan KC) — dùng skill `cost-report`.
