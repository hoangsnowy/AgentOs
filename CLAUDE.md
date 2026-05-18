# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Reference prototype cho luận văn thạc sĩ "Multi-Agent AI cho SDLC" (HUBT, 2026). Kiến trúc **Leader-Specialists-Quality Loop** với 5 agent: Orchestrator, Requirement, Coding, Testing, QA. Hybrid LLM (Anthropic Claude + Azure OpenAI), gán model qua `appsettings.json`. Đầy đủ context + roadmap trong [README.md](README.md). Mô tả từng phase, setup local, secret, GitHub Actions xem [docs/SETUP.md](docs/SETUP.md).

## Commands

```bash
# Build / test (sln-rooted, từ D:\LuanVan\prototype)
dotnet restore AgenticSdlc.sln
dotnet build   AgenticSdlc.sln --configuration Release
dotnet test    AgenticSdlc.sln --configuration Release

# Single test class / method
dotnet test --filter "FullyQualifiedName~ClaudeClientTests"
dotnet test --filter "FullyQualifiedName=AgenticSdlc.Tests.Llm.LlmRequestTests.Validate_AllFieldsValid_DoesNotThrow"

# Run API local — Scalar UI tại http://localhost:5080/scalar/v1
dotnet run --project src/AgenticSdlc.Api

# Secret local (KHÔNG commit). UserSecretsId = "agentic-sdlc-net-prototype"
cd src/AgenticSdlc.Api
dotnet user-secrets set "Llm:Anthropic:ApiKey"  "sk-ant-..."
dotnet user-secrets set "Llm:AzureOpenAI:ApiKey" "..."
```

CI: `.github/workflows/ci.yml` chạy `restore → build Release → test` trên Ubuntu, push `main`/`develop` hoặc PR `main`.

## Architecture

Clean Architecture, 4 layer + tests. Dependency chiều: **Api → Infrastructure → Application → Domain** (chiều ngược chặn bởi project reference).

| Project | Vai trò |
|---|---|
| `AgenticSdlc.Domain` | DTO trung lập provider (`LlmRequest`, `LlmResponse`, `LlmOptions`), interface (`ILlmClient`), exception. Không reference framework nào ngoài .NET BCL. |
| `AgenticSdlc.Application` | Interface tác tử (`IRequirementAgent`, ...) — sẽ thêm Phase 3. Chỉ reference `Microsoft.Extensions.Logging.Abstractions`. |
| `AgenticSdlc.Infrastructure` | Impl `ILlmClient` (3 client), agent impl, DI extension. Reference `Azure.AI.OpenAI`, `Microsoft.Extensions.Http.Resilience`. |
| `AgenticSdlc.Api` | ASP.NET Core minimal API + Scalar UI. Composition root duy nhất. |

### LLM Gateway (critical pattern)

5 agent **không gọi trực tiếp SDK của hãng** — tất cả depend on `ILlmClient` (Domain). `LlmClientFactory` chọn provider theo cấu hình `Agents:<Name>:Provider`. Có 3 impl:

- **`ClaudeClient`** — raw `HttpClient` gọi `POST /v1/messages` Anthropic. Header `x-api-key` + `anthropic-version`. Không dùng SDK của Anthropic.
- **`AzureOpenAiClient`** — raw `HttpClient` gọi `openai/deployments/{model}/chat/completions?api-version=...`. Mặc dù `Azure.AI.OpenAI` package có reference (kế thừa cho `Azure.Identity`), client thực tế viết tay để giữ shape control.
- **`MockLlmClient`** — SHA-256 hash `(model + system + user)` → tra `tests/fixtures/llm/<hash>.json`. Cho test offline / CI không có API key / demo deterministic. Miss → trả `"stub-response"`.

Retry: `RetryPolicy.ExecuteAsync` (exponential 1s/2s/4s, retry 429+5xx+timeout). Không dùng Polly để giữ dependency tối thiểu Sprint 1; chuyển sang `Microsoft.Extensions.Http.Resilience` nếu cần. Client tự ném `TransientHttpException` (internal marker) khi gặp status retry-able, `LlmException` cho non-retriable / malformed.

Cost: `CostCalculator.Calculate(model, in, out)` lookup pricing hardcode (Sonnet 4 / Haiku 4.5 / GPT-4.1 / GPT-4o-mini, USD per 1M token, Q2/2026 snapshot). Match `StartsWith` case-insensitive — cho phép suffix kiểu `claude-sonnet-4-20250514`. Model không match → trả `0m`.

DI: `services.AddLlmGateway(configuration)` (Infrastructure) register named `HttpClient` qua `IHttpClientFactory`, options binding `LlmOptions` từ section `"Llm"`, factory + concrete clients. Default `ILlmClient` resolve qua `ILlmClientFactory.CreateDefault()`.

## Conventions

**.NET 10 / C# 14**, pin `10.0.100` qua `global.json`. `Directory.Build.props` set `TreatWarningsAsErrors=true`, `Nullable=enable`, `AnalysisLevel=latest-recommended`, `InvariantGlobalization=true` (Api override `false`).

**Analyzer suppress** (xem `Directory.Build.props`): `CA1848` + `CA1873` toàn solution (LoggerMessage delegates) — phải gỡ sau Sprint 5. Test project thêm `CA1707` (underscore trong test name), `CA1816`, `CA1859`, `xUnit1051`.

**Test stack**: xUnit **v3** (`xunit.v3` 1.1.0), Shouldly (KHÔNG dùng FluentAssertions vì v8 đã commercial), NSubstitute. Naming: `MethodName_StateUnderTest_ExpectedBehavior`. `TestHttpMessageHandler.cs` là helper stub `HttpClient` cho client test.

**Fixture LLM**: file JSON ở `tests/fixtures/llm/<8-char-hex-hash>.json`, shape `{ "content", "inputTokens", "outputTokens" }` (camelCase). Hash sinh bởi `MockLlmClient.ComputeHash(request)` — 8 byte đầu của SHA-256(`model\n---\nsystem\n---\nuser`).

**Commit**: Conventional Commits tiếng Việt OK (vd `feat(llm):`, `chore: phase N`). Co-author Claude khi pair-coded. CI bắt fail nếu build/test fail Release.

**PR**: template `.github/PULL_REQUEST_TEMPLATE.md` có ô "Tham chiếu đề án" — luôn link tới Mục/Bảng luận văn nếu liên quan.

## Phase status

Roadmap chính trong [README.md](README.md) section "Lộ trình". Khi tick checkbox, commit riêng (`docs: tick Phase N done`). Mỗi phase nên có doc `docs/PHASE_<N>.md` riêng (Phase 1 chưa có, Phase 2 đang chuẩn bị).
