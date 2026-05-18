# So sánh KC v1 (prototype) vs Thesis Bảng 2.6

> Source: `D:\LuanVan\LuanVan_NguyenMinHoang_v3.2.docx` § Bảng 2.6 vs `tests/AgenticSdlc.Tests/bin/Debug/net10.0/TestResults/kc_metrics.csv` (Sprint 4 bench).

## Tóm tắt 1 dòng

**Prototype hiện tại chạy 100% trên MockLlmClient — số liệu là synthetic, KHÔNG so sánh được với Bảng 2.6 (live LLM thật)**. Bảng dưới chỉ ra chính xác chỗ nào lệch + lý do.

## Bảng so sánh

| KC | Metric | Thesis (Bảng 2.6) | Prototype (CSV mock) | Delta | Notes |
|---|---|---|---|---|---|
| **KC1 — Requirement** | Tỷ lệ hoàn thành | 10/10 (100%) | 10/10 (100%) | ✓ match | Cả 2 đều pass tất cả |
| | Độ phủ AC | 92% | n/a | — | Prototype không đo AC coverage, chỉ assert `≥ 2 entities + ≥ 3 AC` |
| | Schema consistency | 100% | 100% | ✓ match | Prototype có JsonSchema validation thật (Task 2) → con số này đáng tin |
| | Latency TB | 3.4 s | 0.123 s | -97% | Mock fix 123ms vs Claude Sonnet 4 thật ~3-5s. KHÔNG so sánh được |
| | Model | Claude Sonnet 4 | `mock-model` | — | Chưa chạy live |
| **KC2 — Coding** | Tỷ lệ hoàn thành | 10/10 (100%) | 10/10 (100%) | ✓ match | |
| | Compile pass | 9/10 | n/a | — | Prototype KHÔNG compile code generated, chỉ assert `files.count >= 3` |
| | Bám requirement | 95% | n/a | — | Prototype KHÔNG đánh giá fidelity req → code |
| | Latency TB | 5.2 s | 0.123 s | -98% | Mock |
| | Model | GPT-4.1 Azure | `mock-model` | — | |
| **KC3 — Testing** | Tỷ lệ hoàn thành | 10/10 (100%) | 10/10 (100%) | ✓ match | |
| | Test chạy được | 87% | n/a | — | Prototype KHÔNG chạy test generated, chỉ assert framework + count + coverage stub `>= 60` |
| | Đủ 3 nhóm test | 10/10 | 10/10 | ✓ match | Fixture có đủ happy + edge + error |
| | Latency TB | 3.1 s | 0.123 s | -96% | Mock |
| | Model | GPT-4o-mini | `mock-model` | — | |
| **KC4 — E2E pipeline** | Tỷ lệ hoàn thành | 9/10 (90%) | 10/10 (100%) | +10% | Prototype luôn pass do Mock không random fail; thesis 1 lần fail do Coding bỏ sót operation thật |
| | Nhất quán req-code-test | 90% | 100% | +10% | QA agent mock luôn `IsConsistent=true` (QaPassJson fixture) |
| | Latency TB | 15.0 s | 0.492 s/iter (= 4 × 123) | -97% | 4 LLM call/iter × mock latency |
| | Iteration TB | n/a | 1 (NMax=1 ép) | — | Mock không trigger QA loop |
| | Model | Mixed (Sonnet + GPT-4.1 + GPT-4o-mini + Haiku) | `mock-model` ×4 | — | |
| **KC5 — Quality Loop** | Tỷ lệ PASS | 10/10 (100%) | 10/10 (100%) | ✓ match | QA fixture luôn `score 0.92, isConsistent=true` |
| | Iteration TB | 1.8 vòng | 1 vòng | -44% | Mock không retry, NMax=1 |
| | Cải thiện điểm/vòng | +18 điểm | n/a | — | Prototype không tracking score delta giữa iter |
| | Model | Claude Haiku 4.5 | `mock-model` | — | |

## Discrepancy analysis (lệch > 20%)

### 1. Latency: prototype -96% đến -98% so thesis

**Root cause**: 100% Mock. `AgentTestHelpers.StubResponse` fix latency 123ms; thesis chạy LLM thật ~3-15s.

**Đây không phải bug — đây là design của bench harness**. KC test prototype validate *flow correctness* (build/parse/validate/route metric), không phải *production latency*. Để có latency thật phải `RUN_LIVE_LLM=1` chạy `LivePipelineSmokeTests` (Task 6 commit `32f3a5f`).

### 2. KC4 pass rate: prototype 100% vs thesis 90%

**Root cause**: Mock orchestrator nhận `QaPassJson` fixture mỗi lần → luôn `IsConsistent=true` → không bao giờ trigger QA loop retry → 100% pass. Thesis có 1/10 case Coding bỏ sót → QA fail iter 1 → retry iter 2 → pass.

**Honest interpretation**: prototype variance = 0 do mock deterministic. Để reproduce thesis variance cần live LLM + temp > 0.

### 3. Iteration TB: prototype 1 vs thesis 1.8

**Root cause**: NMax=1 trong bench (ép orchestrator chạy 1 vòng duy nhất để giữ test fast + cost-bounded). Thesis NMax=3, mean 1.8 do QA loop trigger thực tế.

### 4. Metric thesis có nhưng prototype không đo

- AC coverage % (thesis 92%): cần benchmark agent so với spec ground truth
- Compile pass % (thesis 9/10): cần `dotnet build` trên code generated
- Test executable % (thesis 87%): cần chạy generated tests
- Score improvement / iter (thesis +18): cần track QA score history qua nhiều iter

→ Đây là gap thật, không phải synthetic vs real. Cần Sprint 7 nếu muốn dữ liệu khớp 100% Bảng 2.6.

## Hai path tiếp theo

### Path A — Re-run prototype để match thesis (chạy live LLM)
1. Set `ANTHROPIC_API_KEY` + `AZURE_OPENAI_API_KEY`.
2. Mở rộng `LivePipelineSmokeTests` thành `Kc{1..5}LiveTests` n=10 với provider thật.
3. Thêm AC-coverage + compile-pass + test-executable assertion (cần dotnet build/test trên artifact runtime).
4. Track QA score history qua iteration.
5. Sink CSV `kc_metrics_live.csv` rồi compare lại bảng này.
6. **Effort**: ~6-10h. **Cost**: ~$5-15 (5 KC × 10 iter × ~$0.10).

### Path B — Update thesis Bảng 2.6 để phản ánh prototype hiện tại
- Cập nhật Bảng 2.6 mô tả rõ "kết quả Mock baseline; live LLM benchmark sẽ là phụ lục C".
- Note: thesis hiện đã viết theo giả định kết quả live LLM thật; sửa lại sẽ giảm strength của argument.
- KHÔNG khuyến nghị trừ khi không có budget chạy live.

**Recommendation**: Path A. Live LLM số liệu sẽ chính xác + ấn tượng hơn cho hội đồng. Path B là fallback nếu thiếu key/budget.

## Source files

- Thesis: `D:\LuanVan\LuanVan_NguyenMinHoang_v3.2.docx` § Bảng 2.6 (extract qua `docx2txt`)
- Prototype CSV: `tests/AgenticSdlc.Tests/bin/Debug/net10.0/TestResults/kc_metrics.csv` (80 row sau `dotnet test`)
- Aggregation: `awk -F, 'NR>1 {kc=$3; calls[kc]++; lat[kc]+=$10; cost[kc]+=$11; if($12=="true") pass[kc]++} END {...}'`
