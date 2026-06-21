# AGENTS.md

This file provides guidance to AI coding assistants when working with code in this repository.

## Project

**AgentOS** — a .NET-native multi-agent AI platform for the software development lifecycle. A central orchestrator coordinates 5 specialist agents — Orchestrator, Requirement, Coding, Testing, QA — in a **Leader-Specialists-Quality Loop** (QA scores requirement↔code↔test consistency and loops until convergence or an iteration cap `NMax`). Hybrid, provider-agnostic LLM gateway (Anthropic Claude + Azure OpenAI + Microsoft Agent Framework + a paired dev-machine agent); providers/models are assigned in `appsettings.json`. A Blazor Server **AgentOS** desktop-style UI drives the engine. Full context in [README.md](README.md). For local setup, secrets, and GitHub Actions, see [docs/SETUP.md](docs/SETUP.md).

The platform is a **modular monolith**: each feature is a self-contained `IModule` with its own DI surface, EF Core context, and Postgres schema, so any module can later ship as a standalone NuGet package. Framed as a credible OSS product, not a thesis demo.

## Commands

```bash
# Build / test (slnx-rooted, from D:\LuanVan\prototype)
dotnet restore AgentOs.slnx
dotnet build   AgentOs.slnx --configuration Release
dotnet test    AgentOs.slnx --configuration Release

# Single test class / method
dotnet test AgentOs.slnx --filter "FullyQualifiedName~ToolGatewayTests"
dotnet test AgentOs.slnx --filter "FullyQualifiedName=AgentOs.Tests.Tools.ToolInvocationTests.Request_Validate_AllFieldsValid_DoesNotThrow"

# Run the API locally — Scalar UI at https://localhost:5080/scalar/v1
dotnet run --project src/AgentOs.Api

# Local secrets (DO NOT commit). UserSecretsId = "agentos-prototype"
cd src/AgentOs.Api
dotnet user-secrets set "Llm:Claude:ApiKey"      "sk-ant-..."
dotnet user-secrets set "Llm:AzureOpenAi:ApiKey" "..."
dotnet user-secrets set "Llm:AzureOpenAi:Endpoint" "https://<resource>.openai.azure.com"

# Run the Web (Blazor AgentOS desktop) locally
dotnet run --project src/AgentOs.Web

# One-shot local dev — Aspire AppHost wires Postgres + Keycloak + MailHog + API + Web
dotnet run --project infra/AgentOs.AppHost

# Direct API run without Aspire — provide a Postgres connection string yourself
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=agentos;Username=postgres;Password=postgres"

# Generate a new EF migration — migrations are PER-MODULE (each module is its own startup project,
# owns its DbContext + schema + __EFMigrationsHistory). Example for the Pipeline module:
dotnet ef migrations add <Name> \
  --project src/AgentOs.Modules.Pipeline --startup-project src/AgentOs.Modules.Pipeline \
  --output-dir Persistence/Migrations --context PipelineDbContext
```

CI: `.github/workflows/ci.yml` runs `restore → build Release → test` on Ubuntu, on push to `main`/`develop` or PR to `main`.

## Architecture

Modular monolith. Solution file is **`AgentOs.slnx`** (the .NET 10 XML format). Each module is an `AgentOs.Modules.*` class library that **references `AgentOs.Domain` + `AgentOs.SharedKernel` only**; cross-module references are explicit and minimal (e.g. `Llm → AppConfig`, `Integration → Tools`). Hosts (`Api`, `Web`) reference every module and wire them with one `services.AddModulesFromAssemblies(cfg, …)` call + `await app.Services.InitializeModulesAsync()` + `app.MapModuleEndpoints()` — no per-module wiring in `Program.cs`.

| Project | Role |
|---|---|
| `AgentOs.Domain` | Provider-neutral DTOs (`LlmRequest`, `LlmResponse`, `LlmOptions`), pipeline artifacts (`RequirementSpec`, `CodeArtifact`, `TestArtifact`, `QaReport`, `PipelineResult`), tool contracts (`ITool`, `IToolPolicy`, `IToolInvocationLog`, `IToolGateway`), interfaces (`ILlmClient`, `ILlmClientFactory`), exceptions. References the .NET BCL only. |
| `AgentOs.SharedKernel` | `IModule` / `IEndpointModule` / `IInitializableModule` contracts + `ModuleLoader` (reflection) + cross-cutting `ITenantContext` + `IAuthTokenProvider`. |
| `AgentOs.Modules.AppConfig` | Encrypted runtime KV store (DataProtection), `AppConfigDbContext` (schema `config`). Powers per-tenant LLM key overrides + the Settings UI. |
| `AgentOs.Modules.Llm` | Provider-agnostic gateway: `LlmClientFactory` + keyed `ILlmClient` per provider, `PooledChatLlmClient` (multi-key pool + 429 failover), `CostCalculator`, `AIToolFunction` (ITool→AIFunction adapter). |
| `AgentOs.Modules.Pipeline` | 5 agents + prompts + `PipelineOrchestrator` (+ optional MAF workflow engine) + `PipelineDbContext` (schema `pipeline`). |
| `AgentOs.Modules.Identity` | JWT auth + `HttpTenantContext` (Keycloak claims) + Admin/Member authorization policies. No DbContext, no module-mapped `/auth` route — login is OIDC/cookie via the host. |
| `AgentOs.Modules.Tenants` | Tenant registry + Keycloak admin client (member lifecycle) + signup/invitations + audit, `TenantsDbContext` (schema `tenants`). |
| `AgentOs.Modules.Tools` | `IToolRegistry`, `IToolPolicy` (per-tenant gate), `IToolInvocationLog` (evidence), `IToolGateway` (the policy→invoke→log seam). |
| `AgentOs.Modules.Integration` | `IGitHubPrService` (Octokit) + `IBuildVerifier` (`dotnet build` in a temp dir), exposed as ITools. |
| `AgentOs.Modules.Workspaces` | Connected source workspaces (repo/token registry) + `IWorkspaceConnector`, `WorkspacesDbContext` (schema `workspaces`). |
| `AgentOs.Modules.Sessions` | Remote-runner registry + pairing handshake + session-run feed (`IRunnerPairingService`, `ISessionRunFeed`), `SessionsDbContext` (schema `sessions`). |
| `AgentOs.Modules.Mcp` | MCP client (consume external tool servers) + server adapter; Api also serves MCP at `/mcp`. |
| `AgentOs.Modules.RemoteAgent` | SignalR hub + transport + `RemoteAgentLlmClient` — dispatches work to a paired dev-machine agent (zero server API tokens). |
| `AgentOs.Api` | ASP.NET Core minimal API (REST + Scalar UI + `/mcp`). A composition root. |
| `AgentOs.Web` | Blazor Server **AgentOS** desktop UI (window manager, app catalog, realtime pipeline, orchestration editor). A composition root. |
| `AgentOs.RemoteAgent` | Standalone dev-machine agent executable that pairs to the RemoteAgent hub. |
| `infra/AgentOs.AppHost` | .NET Aspire orchestration (Postgres + Keycloak + MailHog + Api + Web). |

### LLM Gateway (critical pattern)

The 5 agents **do not call vendor SDKs directly** — they depend on `ILlmClient` (Domain). `LlmClientFactory.Create(name)` / `.CreateDefault()` resolve a keyed `ILlmClient` registered under its canonical provider name. Providers:

- **`Claude`** / **`AzureOpenAI`** — `PooledChatLlmClient`: a pool of `Microsoft.Extensions.AI` `IChatClient` instances (Anthropic.SDK / `Azure.AI.OpenAI`) keyed by API key, with round-robin + HTTP 429 cooldown failover across the (runtime override + appsettings) key pool.
- **`MAF`** — Microsoft Agent Framework `ChatClient` (`Microsoft.Agents.AI`).
- **`RemoteAgent`** — dispatches to a paired dev-machine agent over SignalR (owned by `Modules.RemoteAgent`).

Provider selection: `Llm:ForceProvider` / per-agent `Agents:<Name>:Provider` + runtime overrides from the Settings UI (hydrated per request from tenant-scoped `AppConfig`). Cost: `CostCalculator.Calculate(model, in, out)` looks up hardcoded pricing (Sonnet 4 / Haiku 4.5 / GPT-4.1 / GPT-4o-mini, USD per 1M tokens, Q2/2026 snapshot); case-insensitive `StartsWith` match (allows suffixes like `claude-sonnet-4-20250514`); no match → `0m`.

### Tools, policy & evidence (governance)

Agents call **tools** through the gateway: `LlmRequest.Tools = ["build_verifier"]` makes `PooledChatLlmClient` resolve each name via `IToolRegistry`, adapt the `ITool` into an `AIFunction` (`AIToolFunction`), and run the tool-call loop via `FunctionInvokingChatClient`. **Every** invocation passes through `IToolGateway` (`DefaultToolGateway` in Domain) → `IToolPolicy` gate (default-permissive; production reads a per-tenant allowlist) → `ITool.InvokeAsync` → `IToolInvocationLog` (evidence; refusals + errors recorded too). `IToolGateway` is the single server-side seam so remote/off-box execution enforces identical governance. MCP: `Modules.Mcp` registers upstream MCP server tools under `{server}.{tool}`; the Api serves AgentOS's own pipeline as MCP tools at `/mcp`.

### Persistence & multi-tenancy

`ConnectionStrings:DefaultConnection` (Postgres) is the only required wiring; **each module attaches its own DbContext + schema + migration history** (`pipeline.*`, `tenants.*`, `config.*`) and applies migrations at startup via its module init hook. Without a connection string (CI / local), modules fall back to no-op repositories so the app boots stateless. Row-level isolation: tenant-scoped entities carry a `TenantId` + an EF global query filter reading `ITenantContext.TenantId`. `Auth:Mode` switches between `operator` (single pseudo-tenant, HS256) and `keycloak` (RS256, `tenant` claim). Local Postgres via Aspire AppHost; Azure deploy via `azd up` (Container Apps).

## Conventions

**.NET 10 / C# 14**, pinned via `global.json`. `Directory.Build.props` sets `TreatWarningsAsErrors=true`, `Nullable=enable`, `AnalysisLevel=latest-recommended`, `InvariantGlobalization=true` (Api overrides to `false`).

**Analyzer suppressions** (see `Directory.Build.props`): `CA1848` + `CA1873` solution-wide (LoggerMessage delegates) — remove before v1.0. The test project additionally suppresses `CA1707`, `CA1816`, `CA1859`, `xUnit1051`. Modules expose internals via `InternalsVisibleTo("AgentOs.Tests")` where tests need them; `.editorconfig` marks `**/Migrations/*.cs` as `generated_code`.

**Test stack**: xUnit **v3** (`xunit.v3`), Shouldly (NOT FluentAssertions — v8 went commercial), NSubstitute. Naming: `MethodName_StateUnderTest_ExpectedBehavior`.

**Verification (E2E in the running app — unit-green is necessary, NOT sufficient)**: a change with any user-facing value is not "done" until it (1) is wired into the **AgentOS Web desktop** (registered in `AppCatalog` + the `WindowHost` switch), not Api-only, and (2) has been exercised in the *running app* — open the app, run the flow, capture a screenshot as proof. **Every host must run standalone with one command and no external services in Development** — the Web *is* the app; if it can't run without the full Keycloak+Postgres stack, it's broken. `dotnet run --project src/AgentOs.Web` boots the desktop via `Auth:DevAutoLogin` (a Development-only fixed-user auth handler, hard-thrown if ever set outside Development, and forced off under AppHost so the full stack uses real Keycloak); modules fall back to no-op repos with no DB. The UI E2E (`tests/AgentOs.E2E.Tests`, gated by `RUN_AGENTOS_E2E=true`, pointed at `AGENTOS_URL`) runs against that standalone Web. **Standalone proves only the _degraded_ path — match the verification surface to what the change touches.** With `DevAutoLogin` + no-op repos, anything that needs persistence, identity, or multi-tenant silently no-ops (a runner "registers" but is never saved; counters stay 0) or throws (Keycloak admin calls); a green standalone screenshot is NOT proof for those features. Rule: pure UI/layout → standalone screenshot is sufficient; **persistence / auth / tenant → verify on the full Aspire stack** (`dotnet run --project infra/AgentOs.AppHost`; dashboard `:15050`, Web `https://localhost:5180`, seeded login `operator`/`operator` on realm `agentic`) **and hand the user a drivable URL + creds so they can confirm too — never sign off a real-service feature on a surface only you can drive.** Per-circuit UI state (WindowManagerService, ToastService) is **scoped**, never singleton, or it bleeds across users. Run the full stack with `dotnet run --project infra/AgentOs.AppHost` (Aspire wires Postgres + Keycloak + Api + Web). Note the circuit constraint: a Blazor interactive-Server component has no `HttpContext`, so it can't use `ITenantContext` — read the tenant from `AuthenticationState` and pass it explicitly, or call a tenant-explicit service. Do NOT defer a feature's UI to a far-off milestone without explicit sign-off — every milestone must show something in the desktop.

**UI / design**: before adding or editing any screen/component, read [docs/design/design-system.md](docs/design/design-system.md) **or run `/design-review`** — it is the single source of truth. Reuse the **defined** vocabulary (`Btn`/`Field`/`Panel`/`Icon` components; the `prefs-*`, `.page-head`, `.settings-table` classes SystemApp uses; `var(--…)` tokens). **Never reference a CSS class that is not defined in `app.css` or a scoped `.razor.css`** — phantom classes render unstyled and were the root cause of past Spine/Users layout breakage; `/design-review` greps `.razor` class tokens against defined CSS to catch them. Surface chrome outside the shell (the Keycloak login at `infra/keycloak/themes/agentos`) mirrors the same tokens + branding (it is **AgentOS**, never "AgentOs").

**Commits**: Conventional Commits in English (e.g. `feat(tools):`, `fix(llm):`, `chore(deps):`). Co-author Claude when pair-coded. CI fails if the Release build/test fails.

**Language**: code, comments, docs, and LLM prompts/output are English (the repo was standardized from Vietnamese). The agent system prompts (`Modules.Pipeline/Prompts/*.cs`) open with `"You are the <X> Agent …"` — this opening line is used as a routing key; keep the prompt and any matcher in sync if reworded.

**Branding**: the product + desktop UI are both **AgentOS**. Code namespaces stay `AgentOs.*`. Do not reintroduce "Agent Studio" or the retired `AgenticSdlc.*` prefix.

**PR**: fill in `.github/PULL_REQUEST_TEMPLATE.md` (summary + test plan) on every pull request.
