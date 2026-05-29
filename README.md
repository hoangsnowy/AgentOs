# agentic-sdlc-net

> A **.NET-native multi-agent AI framework** for the software development lifecycle. A central orchestrator coordinates specialist agents — requirements → code → tests → QA — in a **Leader · Specialists · Quality Loop**, on a **hybrid LLM** backend (Anthropic Claude + Azure OpenAI), all kept behind one provider-agnostic interface.

[![CI](https://github.com/hoangsnowy/agentic-sdlc-net/actions/workflows/ci.yml/badge.svg)](https://github.com/hoangsnowy/agentic-sdlc-net/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

`agentic-sdlc-net` turns a plain-English user story into reviewed, test-backed C# scaffolding. Five agents collaborate under a central orchestrator; a QA agent scores requirement–code–test consistency and loops until it converges or hits an iteration cap. Built on **.NET 10** and **Clean Architecture**, with a provider-agnostic LLM gateway so you can mix vendors per agent — or swap them entirely — from config alone.

## Features

- **Leader · Specialists · Quality Loop** — the orchestrator assigns work to four specialists; the QA agent gates the result and drives re-runs until consistent or `NMax` is reached.
- **Hybrid LLM, provider-agnostic** — each agent's provider/model lives in `appsettings.json`; agents depend on `ILlmClient`, never on a vendor SDK directly.
- **Built-in clients** — Anthropic Claude (`/v1/messages`) and Azure OpenAI (`chat/completions`), plus a deterministic `MockLlmClient` for offline runs and CI.
- **Per-call telemetry** — token, latency and an estimated USD cost on every LLM call.
- **Agent Studio (Blazor)** — a realtime web UI to watch the pipeline run live, plus a visual workflow editor.
- **Optional persistence** — EF Core / Postgres; boots stateless when no connection string is set.
- **Resilient** — retry with exponential backoff on 429 / 5xx / timeout.
- **Cloud-ready** — .NET Aspire AppHost + `azd up` to Azure Container Apps.

## Agents

| Agent | Role | Default model |
|---|---|---|
| **Orchestrator** | Coordinates the flow, assigns tasks, aggregates the result | Claude Haiku 4.5 |
| **Requirement** | User story → structured requirements (JSON) | Claude Sonnet 4 |
| **Coding** | Generate C# scaffolding (Clean Architecture) | GPT-4.1 (Azure) |
| **Testing** | Generate xUnit tests (happy / edge / error) | GPT-4o-mini (Azure) |
| **QA** | Score requirement–code–test consistency, drive the loop | Claude Haiku 4.5 |

Those are illustrative defaults — reassign any agent to any provider in config.

## Architecture

Clean Architecture; dependencies point inward (`Api`/`Web` → `Infrastructure` → `Application` → `Domain`).

```
src/
├── AgenticSdlc.Domain/          # DTOs, pipeline artifacts, ILlmClient, exceptions (BCL only)
├── AgenticSdlc.Application/     # Agent interfaces, prompts, metrics + repository contracts
├── AgenticSdlc.Infrastructure/  # LLM clients, agent + orchestrator impls, EF Core, DI
├── AgenticSdlc.Api/             # ASP.NET Core minimal API (+ Scalar UI)
├── AgenticSdlc.Web/             # Blazor Server "Agent Studio"
├── AgenticSdlc.AppHost/         # .NET Aspire orchestration
└── AgenticSdlc.ServiceDefaults/ # Shared telemetry / health / resilience
tests/                           # xUnit unit, integration + E2E
```

**LLM Gateway** is the core abstraction. Agents depend on `ILlmClient`; `LlmClientFactory` picks the implementation from `Agents:<Name>:Provider`. Swapping a provider is a config change, not a code change.

## Quick start

Prerequisites: **.NET 10 SDK** (pinned via `global.json`). Optional: Docker (local Postgres), and an Anthropic and/or Azure OpenAI key — the Demo path needs neither.

```bash
git clone https://github.com/hoangsnowy/agentic-sdlc-net.git
cd agentic-sdlc-net

dotnet restore AgenticSdlc.sln
dotnet build   AgenticSdlc.sln -c Release
dotnet test    AgenticSdlc.sln -c Release
```

Run the API — Scalar UI at `http://localhost:5080/scalar/v1`:

```bash
dotnet run --project src/AgenticSdlc.Api
```

Run the Agent Studio web UI:

```bash
dotnet run --project src/AgenticSdlc.Web
```

Or run the whole stack with the Aspire dashboard:

```bash
dotnet run --project src/AgenticSdlc.AppHost
```

Call the end-to-end pipeline:

```bash
curl -X POST http://localhost:5080/pipeline \
  -H "Content-Type: application/json" \
  -d '{"userStory":"An admin can create, view, edit and delete products; users browse by category.","nMax":3}'
```

## Configuration

Set secrets via user-secrets — never commit keys:

```bash
cd src/AgenticSdlc.Api
dotnet user-secrets set "Llm:Anthropic:ApiKey"   "sk-ant-..."
dotnet user-secrets set "Llm:AzureOpenAI:ApiKey" "..."
```

Per-agent provider/model mapping (`appsettings.json`):

```json
{
  "Llm": {
    "Anthropic":   { "BaseUrl": "https://api.anthropic.com", "Version": "2023-06-01" },
    "AzureOpenAI": { "Endpoint": "https://<resource>.openai.azure.com" }
  },
  "Agents": {
    "Orchestrator": { "Provider": "Anthropic",   "Model": "claude-haiku-4-5", "Temperature": 0.3, "MaxTokens": 2000 },
    "Requirement":  { "Provider": "Anthropic",   "Model": "claude-sonnet-4",  "Temperature": 0.1, "MaxTokens": 2000 },
    "Coding":       { "Provider": "AzureOpenAI", "Model": "gpt-4.1",          "Temperature": 0.2, "MaxTokens": 4000 },
    "Testing":      { "Provider": "AzureOpenAI", "Model": "gpt-4o-mini",      "Temperature": 0.2, "MaxTokens": 3000 },
    "Qa":           { "Provider": "Anthropic",   "Model": "claude-haiku-4-5", "Temperature": 0.1, "MaxTokens": 1500 }
  }
}
```

No keys handy? Run **Demo mode** in Agent Studio (backed by `MockLlmClient`) for a fully offline pipeline.

### Optional: persistence

Provide a connection string to enable EF Core / Postgres (otherwise the app runs stateless):

```bash
docker compose up -d
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=agentic_sdlc;Username=postgres;Password=postgres"
```

## API

| Method | Path | Description |
|---|---|---|
| `POST` | `/requirement` | Run the Requirement agent on its own |
| `POST` | `/code` | Run the Coding agent on its own |
| `POST` | `/test` | Run the Testing agent on its own |
| `POST` | `/qa` | Run the QA agent on its own |
| `POST` | `/pipeline` | Run the full end-to-end flow |
| `GET`  | `/health` | Health check |

## Deploy

```bash
azd up   # provisions + deploys to Azure Container Apps via the Aspire AppHost
```

## Roadmap

The project is growing from a fixed 5-agent pipeline into a general, **governance-first** agentic platform: pluggable agent runtimes (incl. Semantic Kernel), MCP-based tool execution behind a sandbox + approval gate, an evidence/lineage store, and durable workflows — with the SDLC pipeline as the flagship example. Full plan in [docs/ROADMAP_PLATFORM_V2.md](docs/ROADMAP_PLATFORM_V2.md).

## Phases

| Phase | Title | Status |
| --- | --- | --- |
| 1 | Domain + LLM Gateway | done |
| 2 | 5 agents + JSON schemas | done |
| 3 | Pipeline orchestrator (Leader–Specialists–Quality Loop) | done |
| 4 | Minimal API + Scalar | done |
| 5 | Unit tests + KC1–KC5 benchmark harness | done |
| 6 | Azure deploy IaC (Bicep + Container Apps) | done (no live deploy yet) |
| 7 | Blazor Agent Studio + AgentOS desktop chrome | done |
| **8** | **Web ↔ API production separation + auth + DB secrets** | **in progress (8.1 landed)** — see [docs/PHASE_8.md](docs/PHASE_8.md) |

## Contributing

Issues and PRs are welcome. Build and test with `dotnet test AgenticSdlc.sln -c Release`; CI runs the same on every push and PR. Commits follow [Conventional Commits](https://www.conventionalcommits.org/). Code, comments and docs are English; `Nullable` and `TreatWarningsAsErrors` are enabled across the solution.

## License

[MIT](LICENSE)
