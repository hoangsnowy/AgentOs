# Comparison: KC v1 (prototype) vs Thesis Table 2.6

> Source: `D:\LuanVan\LuanVan_NguyenMinHoang_v3.2.docx` § Table 2.6 vs `tests/AgenticSdlc.Tests/bin/Debug/net10.0/TestResults/kc_metrics.csv` (Sprint 4 bench).

## One-line summary

**The current prototype runs 100% on MockLlmClient — the figures are synthetic and NOT comparable to Table 2.6 (real live LLM)**. The table below points out exactly where the discrepancies are + why.

## Comparison table

| KC | Metric | Thesis (Table 2.6) | Prototype (mock CSV) | Delta | Notes |
|---|---|---|---|---|---|
| **KC1 — Requirement** | Completion rate | 10/10 (100%) | 10/10 (100%) | ✓ match | Both pass everything |
| | AC coverage | 92% | n/a | — | The prototype does not measure AC coverage, only asserts `≥ 2 entities + ≥ 3 AC` |
| | Schema consistency | 100% | 100% | ✓ match | The prototype has real JsonSchema validation (Task 2) → this figure is trustworthy |
| | Avg latency | 3.4 s | 0.123 s | -97% | Mock fixed at 123ms vs real Claude Sonnet 4 ~3-5s. NOT comparable |
| | Model | Claude Sonnet 4 | `mock-model` | — | Not run live yet |
| **KC2 — Coding** | Completion rate | 10/10 (100%) | 10/10 (100%) | ✓ match | |
| | Compile pass | 9/10 | n/a | — | The prototype does NOT compile the generated code, only asserts `files.count >= 3` |
| | Requirement fidelity | 95% | n/a | — | The prototype does NOT assess req → code fidelity |
| | Avg latency | 5.2 s | 0.123 s | -98% | Mock |
| | Model | GPT-4.1 Azure | `mock-model` | — | |
| **KC3 — Testing** | Completion rate | 10/10 (100%) | 10/10 (100%) | ✓ match | |
| | Tests runnable | 87% | n/a | — | The prototype does NOT run the generated tests, only asserts framework + count + coverage stub `>= 60` |
| | All 3 test groups present | 10/10 | 10/10 | ✓ match | The fixture has happy + edge + error |
| | Avg latency | 3.1 s | 0.123 s | -96% | Mock |
| | Model | GPT-4o-mini | `mock-model` | — | |
| **KC4 — E2E pipeline** | Completion rate | 9/10 (90%) | 10/10 (100%) | +10% | The prototype always passes because Mock does not randomly fail; the thesis had 1 failure due to Coding actually omitting an operation |
| | Req-code-test consistency | 90% | 100% | +10% | The mock QA agent is always `IsConsistent=true` (QaPassJson fixture) |
| | Avg latency | 15.0 s | 0.492 s/iter (= 4 × 123) | -97% | 4 LLM calls/iter × mock latency |
| | Avg iterations | n/a | 1 (NMax=1 forced) | — | Mock does not trigger the QA loop |
| | Model | Mixed (Sonnet + GPT-4.1 + GPT-4o-mini + Haiku) | `mock-model` ×4 | — | |
| **KC5 — Quality Loop** | PASS rate | 10/10 (100%) | 10/10 (100%) | ✓ match | The QA fixture is always `score 0.92, isConsistent=true` |
| | Avg iterations | 1.8 iterations | 1 iteration | -44% | Mock does not retry, NMax=1 |
| | Score improvement/iter | +18 points | n/a | — | The prototype does not track the score delta between iterations |
| | Model | Claude Haiku 4.5 | `mock-model` | — | |

## Discrepancy analysis (deviation > 20%)

### 1. Latency: prototype -96% to -98% vs thesis

**Root cause**: 100% Mock. `AgentTestHelpers.StubResponse` fixes latency at 123ms; the thesis runs a real LLM at ~3-15s.

**This is not a bug — it is the design of the bench harness**. The prototype KC tests validate *flow correctness* (build/parse/validate/route metrics), not *production latency*. To get real latency, you must run `LivePipelineSmokeTests` with `RUN_LIVE_LLM=1` (Task 6 commit `32f3a5f`).

### 2. KC4 pass rate: prototype 100% vs thesis 90%

**Root cause**: the mock orchestrator receives the `QaPassJson` fixture every time → always `IsConsistent=true` → never triggers a QA loop retry → 100% pass. The thesis had 1/10 cases where Coding omitted something → QA failed iter 1 → retried iter 2 → passed.

**Honest interpretation**: prototype variance = 0 because the mock is deterministic. Reproducing the thesis variance requires a live LLM + temp > 0.

### 3. Avg iterations: prototype 1 vs thesis 1.8

**Root cause**: NMax=1 in the bench (forcing the orchestrator to run a single iteration to keep tests fast + cost-bounded). The thesis uses NMax=3, mean 1.8, because the QA loop triggers in practice.

### 4. Metrics the thesis has but the prototype does not measure

- AC coverage % (thesis 92%): requires benchmarking the agent against a spec ground truth
- Compile pass % (thesis 9/10): requires `dotnet build` on the generated code
- Test executable % (thesis 87%): requires running the generated tests
- Score improvement / iter (thesis +18): requires tracking QA score history across iterations

→ This is a real gap, not synthetic vs real. Sprint 7 would be needed for the data to match Table 2.6 100%.

## Two next paths

### Path A — Re-run the prototype to match the thesis (run a live LLM)
1. Set `ANTHROPIC_API_KEY` + `AZURE_OPENAI_API_KEY`.
2. Extend `LivePipelineSmokeTests` into `Kc{1..5}LiveTests` n=10 with a real provider.
3. Add AC-coverage + compile-pass + test-executable assertions (requires dotnet build/test on the runtime artifact).
4. Track QA score history across iterations.
5. Sink the CSV `kc_metrics_live.csv` then compare against this table again.
6. **Effort**: ~6-10h. **Cost**: ~$5-15 (5 KC × 10 iter × ~$0.10).

### Path B — Update thesis Table 2.6 to reflect the current prototype
- Update Table 2.6 to clearly state "Mock baseline results; the live LLM benchmark will be Appendix C".
- Note: the thesis is currently written assuming real live LLM results; changing it would weaken the argument.
- NOT recommended unless there is no budget to run live.

**Recommendation**: Path A. Live LLM figures will be more accurate + more impressive to the committee. Path B is the fallback if a key/budget is unavailable.

## Source files

- Thesis: `D:\LuanVan\LuanVan_NguyenMinHoang_v3.2.docx` § Table 2.6 (extracted via `docx2txt`)
- Prototype CSV: `tests/AgenticSdlc.Tests/bin/Debug/net10.0/TestResults/kc_metrics.csv` (80 rows after `dotnet test`)
- Aggregation: `awk -F, 'NR>1 {kc=$3; calls[kc]++; lat[kc]+=$10; cost[kc]+=$11; if($12=="true") pass[kc]++} END {...}'`
