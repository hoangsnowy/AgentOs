# Phase 5 — Unit tests + benchmark KC1-KC5

> Status: ✅ Done — 2026-05-18

## Mục tiêu

Hoàn thiện coverage test cho 5 agent + orchestrator, bổ sung dataset KC1-KC5 (Mục 2.5 luận văn) và bench harness để chạy thực nghiệm offline (Mock provider) hoặc online (Claude / Azure OpenAI).

## Deliverable

### Tests

- `tests/AgenticSdlc.Tests/Pipeline/SequencedLlmClient.cs` — test `ILlmClient` trả response theo thứ tự gọi (in-memory queue, capture request cho assertion).
- `tests/AgenticSdlc.Tests/Pipeline/PipelineEndToEndTests.cs` — 3 case E2E:
  - `Pipeline_QaPassFirstIteration_AggregatesAllMetrics` — 4 canned response, pass ngay vòng 1, verify tổng metric.
  - `Pipeline_QaFailsThenPasses_TwoIterations` — 7 canned response, vòng 1 QA fail → vòng 2 pass.
  - `Pipeline_MalformedRequirementResponse_ReturnsFailed` — verify failure mode.

Tổng test count: **80** (+3 từ Phase 4).

### KC dataset

`tests/fixtures/kc/kc{1-5}.json` — mỗi file mảng `{id, input, expected}`. README mô tả convention + cách thêm case.

### Bench harness

Skill `/kc-bench` (đã ship Phase 2-skills) là entrypoint chạy bench. Skill tự:
1. Start API local nếu cần (`dotnet run --project src/AgenticSdlc.Api`).
2. Loop dataset × N iteration (default 3).
3. POST request, capture metric từ response body.
4. Aggregate → `.xlsx` + `.md` report dùng skill `xlsx` của Anthropic.

Bench KHÔNG implement standalone tool — leveraging skill stack đã có, tránh trùng lặp.

## Quyết định kỹ thuật

- **Tại sao SequencedLlmClient chứ không dùng MockLlmClient (hash-based)?**
  Hash trong `MockLlmClient.ComputeHash` phụ thuộc exact prompt content. CodingAgent prompt include `RequirementSpec` JSON-serialized → nếu RequirementAgent output thay đổi (kể cả whitespace), hash CodingAgent thay đổi → fixture brittle. Sequenced client trade off determinism (canned per call) lấy stability cho E2E test.

- **Tại sao KC5 ngưỡng `PassScore = 0.8` mà không cao hơn?**
  Mục 2.4.5 luận văn — đủ thực dụng cho prototype, tránh QA loop chạy nhiều iteration với LLM Sonnet 4 (cost). Có thể nâng lên 0.85 nếu pass-rate KC4 > 70%.

- **Tại sao KC2/3/5 dùng `specFixture` reference?**
  Tách dataset thành DAG (KC1 → KC2 → KC3 → KC5; KC4 chạy độc lập). Cho phép thay 1 fixture mà không re-generate cả chain. Khi không có fixture, bench skill skip case + báo.

## Tham chiếu luận văn

- Mục 2.5 — Kịch bản thực nghiệm KC1-KC5
- Mục 3.3 — Phương pháp đo (token, cost, latency, pass-rate)
- Mục 4.2 — Báo cáo kết quả benchmark (sẽ điền sau khi chạy bench thật)

## Phase tiếp theo

→ Phase 6 — Azure deployment (Container Apps + App Insights). Build Docker image, Bicep IaC, GitHub Actions deploy workflow.
