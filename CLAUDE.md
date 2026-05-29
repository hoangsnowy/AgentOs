# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

A .NET-native multi-agent AI platform for the software development lifecycle. The **Leader-Specialists-Quality Loop** architecture with 5 agents: Orchestrator, Requirement, Coding, Testing, QA. Hybrid LLM (Anthropic Claude + Azure OpenAI), with models assigned via `appsettings.json`. A Blazor **AgentOS** desktop UI drives the engine. Full context in [README.md](README.md). For local setup, secrets, and GitHub Actions, see [docs/SETUP.md](docs/SETUP.md).

## Commands

```bash
# Build / test (sln-rooted, from D:\LuanVan\prototype)
dotnet restore AgentOs.sln
dotnet build   AgentOs.sln --configuration Release
dotnet test    AgentOs.sln --configuration Release

# Single test class / method
dotnet test --filter "FullyQualifiedName~ClaudeClientTests"
dotnet test --filter "FullyQualifiedName=AgentOs.Tests.Llm.LlmRequestTests.Validate_AllFieldsValid_DoesNotThrow"

# Run the API locally — Scalar UI at http://localhost:5080/scalar/v1
dotnet run --project src/AgentOs.Api

# Local secrets (DO NOT commit). UserSecretsId = "agentos-prototype"
cd src/AgentOs.Api
dotnet user-secrets set "Llm:Anthropic:ApiKey"  "sk-ant-..."
dotnet user-secrets set "Llm:AzureOpenAI:ApiKey" "..."

# Run the Web (Blazor "Agent Studio") locally
dotnet run --project src/AgentOs.Web

# One-shot local dev — Aspire AppHost wires Postgres + Keycloak + API + Web
dotnet run --project infra/AgentOs.AppHost

# Direct API run without Aspire — provide a Postgres connection string yourself
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=agentos;Username=postgres;Password=postgres"

# Generate a new EF migration (Infrastructure is its own startup project for this)
dotnet ef migrations add <Name> --project src/AgentOs.Infrastructure --startup-project src/AgentOs.Infrastructure --output-dir Persistence/Migrations
```

CI: `.github/workflows/ci.yml` runs `restore → build Release → test` on Ubuntu, on push to `main`/`develop` or PR to `main`.

## Architecture

Clean Architecture (Domain → Application → Infrastructure) with two hosts (Api, Web). Dependency direction: **Api/Web → Infrastructure → Application → Domain** (the reverse direction is blocked by project references).

| Project | Role |
|---|---|
| `AgentOs.Domain` | Provider-neutral DTOs (`LlmRequest`, `LlmResponse`, `LlmOptions`), pipeline artifacts (`RequirementSpec`, `CodeArtifact`, `TestArtifact`, `QaReport`, `PipelineResult`), interfaces (`ILlmClient`), exceptions. References no framework other than the .NET BCL. |
| `AgentOs.Application` | Agent interfaces (`IRequirementAgent` … `IOrchestratorAgent`), prompts, metrics (`IMetricsCollector`), persistence repository interfaces (`IPipelineRunRepository`, `IOrchestrationRepository`). References only `Microsoft.Extensions.Logging.Abstractions`. |
| `AgentOs.Infrastructure` | `ILlmClient` implementations (3 clients), 5 agent + orchestrator implementations, EF Core/Postgres persistence, DI extensions. References `Azure.AI.OpenAI`, `Microsoft.Extensions.Http.Resilience`, `Npgsql.EntityFrameworkCore.PostgreSQL`. |
| `AgentOs.Api` | ASP.NET Core minimal API (REST endpoints + Scalar UI). A composition root. |
| `AgentOs.Web` | Blazor Server "Agent Studio" UI (realtime pipeline + orchestration editor). Runs the engine in-process; deployed as a second Container App. A composition root. |

### LLM Gateway (critical pattern)

The 5 agents **do not call vendor SDKs directly** — they all depend on `ILlmClient` (Domain). `LlmClientFactory` selects the provider based on the `Agents:<Name>:Provider` configuration. There are 3 implementations:

- **`ClaudeClient`** — a raw `HttpClient` calling Anthropic's `POST /v1/messages`. Headers `x-api-key` + `anthropic-version`. Does not use the Anthropic SDK.
- **`AzureOpenAiClient`** — a raw `HttpClient` calling `openai/deployments/{model}/chat/completions?api-version=...`. Although the `Azure.AI.OpenAI` package is referenced (inherited for `Azure.Identity`), the client itself is hand-written to retain shape control.
Retry: `RetryPolicy.ExecuteAsync` (exponential 1s/2s/4s, retries 429+5xx+timeout). Polly is not used in order to keep dependencies minimal initially; switch to `Microsoft.Extensions.Http.Resilience` if needed. The client throws `TransientHttpException` (an internal marker) on retry-able statuses, and `LlmException` for non-retriable / malformed cases.

Cost: `CostCalculator.Calculate(model, in, out)` looks up hardcoded pricing (Sonnet 4 / Haiku 4.5 / GPT-4.1 / GPT-4o-mini, USD per 1M tokens, Q2/2026 snapshot). Matches with a case-insensitive `StartsWith` — allowing suffixes like `claude-sonnet-4-20250514`. A model with no match → returns `0m`.

DI: `services.AddLlmGateway(configuration)` (Infrastructure) registers a named `HttpClient` via `IHttpClientFactory`, binds `LlmOptions` from the `"Llm"` section, and registers the factory + concrete clients. The default `ILlmClient` is resolved via `ILlmClientFactory.CreateDefault()`.

### Persistence (optional)

`services.AddPersistence(configuration)` (Infrastructure) registers an EF Core `AgentOsDbContext` (Postgres/Npgsql) + repositories **when `ConnectionStrings:DefaultConnection` is set**; otherwise it registers no-op repos so the app still boots stateless (CI / local without a DB). Three tables: `pipeline_runs` (artifact as `jsonb`), `run_metrics` (one row per LLM call — SQL-friendly for analytics), `orchestrations` (Agent Studio state). `PersistingOrchestratorAgent` decorates `IOrchestratorAgent` to save each run + per-call metrics best-effort (a DB error never corrupts a successful run). `await app.Services.InitializePersistenceAsync()` applies EF migrations at startup. Local Postgres: `docker compose up -d`. Azure: bicep `deployPostgres=true` (default off — avoids cost).

## Conventions

**.NET 10 / C# 14**, pinned to `10.0.100` via `global.json`. `Directory.Build.props` sets `TreatWarningsAsErrors=true`, `Nullable=enable`, `AnalysisLevel=latest-recommended`, `InvariantGlobalization=true` (Api overrides to `false`).

**Analyzer suppressions** (see `Directory.Build.props`): `CA1848` + `CA1873` across the whole solution (LoggerMessage delegates) — must be removed before v1.0. The test project additionally suppresses `CA1707` (underscores in test names), `CA1816`, `CA1859`, `xUnit1051`. Persistence-related: `Infrastructure.csproj` suppresses two `NU1903` advisories for `System.Security.Cryptography.Xml` (a build-time-only transitive of `Microsoft.EntityFrameworkCore.Design`, not shipped at runtime, no patch yet) and exposes internals via `InternalsVisibleTo("AgentOs.Tests")`; `.editorconfig` marks `**/Migrations/*.cs` as `generated_code` (EF migrations use block-scoped namespaces).

**Test stack**: xUnit **v3** (`xunit.v3` 1.1.0), Shouldly (NOT FluentAssertions, since v8 went commercial), NSubstitute. Naming: `MethodName_StateUnderTest_ExpectedBehavior`. `TestHttpMessageHandler.cs` is a helper that stubs `HttpClient` for client tests.

**Commits**: Conventional Commits in English (e.g. `feat(llm):`, `fix(infra):`, `chore(deps):`). Co-author Claude when pair-coded. CI fails if the Release build/test fails.

**Language**: code, comments, docs, and LLM prompts/output are English (the repo was standardized from Vietnamese). The agent system prompts (`Application/Prompts/*.cs`) open with `"You are the <X> Agent …"` — this opening line is a routing key matched by `DemoLlmClient` + `OrchestrationStudio`; keep all three in sync if reworded.

**PR**: fill in `.github/PULL_REQUEST_TEMPLATE.md` (summary + test plan) on every pull request.
