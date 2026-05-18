# KC Dataset — Mục 2.5 luận văn

5 file dataset cho kịch bản thực nghiệm KC1-KC5. Mỗi file = mảng test case `{id, input, expected}`.

| File | KC | Endpoint chạy | Metric đo |
|---|---|---|---|
| `kc1.json` | KC1 — Requirement Analysis | `POST /requirement` | tokens, cost, entitiesCount, endpointsCount, acceptanceCriteriaCount |
| `kc2.json` | KC2 — Code Generation | `POST /code` | tokens, cost, filesCount, must-have class/route |
| `kc3.json` | KC3 — Test Generation | `POST /test` | tokens, cost, happy/edge/error count, framework, coverage |
| `kc4.json` | KC4 — Pipeline End-to-End | `POST /pipeline` | total tokens/cost, iteration count, final status, qa score |
| `kc5.json` | KC5 — QA Consistency | `POST /qa` | tokens, cost, score, isConsistent, issues count |

## Cách chạy

Dùng skill `/kc-bench`:

```
/kc-bench all              # tất cả 5 KC, default provider Mock
/kc-bench KC4              # chỉ KC4 (pipeline)
/kc-bench KC1,KC4 --real   # chạy thật với Claude+Azure (cost warning)
```

Skill tự:
1. Start API local nếu chưa chạy.
2. Loop từng case × N lặp (default 3).
3. POST request, đo metric.
4. Aggregate → `docs/bench/kc-summary-{date}.xlsx` + markdown.

## Dependency fixture (kc2/kc3/kc5)

KC2/KC3/KC5 cần output từ KC trước (vd KC2 cần `RequirementSpec` từ KC1).
Trường `specFixture`, `codeFixture`, `testsFixture` trỏ tới snapshot fixture
đã được record (skill `/fixture-record` sinh các file này).

Nếu fixture chưa tồn tại, bench skill tự skip case + báo cáo lý do.

## Mở rộng

Thêm case mới: append vào JSON, đặt `id` unique (vd `KC1-004`).
KHÔNG đổi shape `expected` mà không update bench harness song song.
