# agentic-sdlc-net

> A **.NET-native multi-agent AI platform** for the software development lifecycle. A central
> orchestrator coordinates specialist agents — requirements → code → tests → QA — in a
> **Leader · Specialists · Quality Loop**, on a **hybrid LLM** backend (Anthropic Claude +
> Azure OpenAI) behind one provider-agnostic interface, with a Blazor **AgentOS** desktop UI.

[![CI](https://github.com/hoangsnowy/agentic-sdlc-net/actions/workflows/ci.yml/badge.svg)](https://github.com/hoangsnowy/agentic-sdlc-net/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
[![PRs welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)

`agentic-sdlc-net` turns a plain-English user story into reviewed, test-backed C# scaffolding.
Five agents collaborate under a central orchestrator; a QA agent scores requirement–code–test
consistency and loops until it converges or hits an iteration cap. Built on **.NET 10** and
**Clean Architecture**, with a provider-agnostic LLM gateway so you can mix vendors per agent —
or swap them entirely — from config alone.

> **Status:** pre-1.0, actively developed. The core pipeline, gateway, and AgentOS UI are working;
> APIs may still change before a `v1.0` tag.

**Contents:** [Features](#features) · [Agents](#agents) · [Architecture](#architecture) ·
[Quick start](#quick-start) · [Configuration](#configuration) · [API](#api) · [Deploy](#deploy) ·
[Contributing](#contributing)

## Features

- **Leader · Specialists · Quality Loop** — the orchestrator assigns work to four specialists; the QA agent gates the result and drives re-runs until consistent or `NMax` is reached.
- **Hybrid LLM, provider-agnostic** — each agent's provider/model lives in `appsettings.json`; agents depend on `ILlmClient`, never on a vendor SDK directly.
- **Built-in clients** — Anthropic Claude (`/v1/messages`) and Azure OpenAI (`chat/completions`), plus a deterministic `MockLlmClient` for offline runs and CI.
- **AgentOS desktop (Blazor)** — a windowed web UI: a Start menu, dock, light/dark themes, and apps for the live pipeline, a visual workflow editor, settings, and system preferences.
- **Verify build + Open PR** — compile the generated code in a sandbox (`dotnet build`) and open a pull request with it straight from the Pipeline app.
- **Per-call telemetry** — token, latency, and an estimated USD cost on every LLM call.
- **Client–server or single-process** — the Web can drive a remote API over SSE, or run the engine in-process for local/offline use.
- **Bearer auth** — JWT-protected API endpoints; sign-in overlay in the Web.
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
├── AgenticSdlc.Api/             # ASP.NET Core minimal API (+ Scalar UI), JWT auth
├── AgenticSdlc.Web/             # Blazor Server "AgentOS" desktop
├── AgenticSdlc.AppHost/         # .NET Aspire orchestration
└── AgenticSdlc.ServiceDefaults/ # Shared telemetry / health / resilience
tests/                           # xUnit unit, integration + E2E
```

**LLM Gateway** is the core abstraction. Agents depend on `ILlmClient`; `LlmClientFactory` picks
the implementation from `Agents:<Name>:Provider`. Swapping a provider is a config change, not a
code change.

## Quick start

Prerequisites: **.NET 10 SDK** (pinned via `global.json`). Optional: Docker (local Postgres), and
an Anthropic and/or Azure OpenAI key — the offline Demo path needs neither.

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

Run the AgentOS desktop UI at `http://localhost:5180`:

```bash
dotnet run --project src/AgenticSdlc.Web
```

By default the Web runs the engine **in-process** (no API needed). To point it at a remote API
instead, set `Api:BaseUrl`:

```bash
$env:Api__BaseUrl = "http://localhost:5080/"   # PowerShell
export Api__BaseUrl="http://localhost:5080/"    # bash
```

Or run the whole stack with the Aspire dashboard:

```bash
dotnet run --project src/AgenticSdlc.AppHost
```

Call the end-to-end pipeline directly:

```bash
curl -X POST http://localhost:5080/pipeline \
  -H "Content-Type: application/json" \
  -d '{"userStory":"An admin can create, view, edit and delete products; users browse by category.","nMax":3}'
```

> No keys handy? The AgentOS desktop runs a fully offline pipeline backed by `MockLlmClient`.

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

LLM keys, the forced provider, and the GitHub integration can also be set at runtime from the
**Settings** app in AgentOS.

#### Multiple keys + rate-limit failover

Give a provider a pool of keys and the gateway round-robins across them, cooling any key that
returns HTTP 429 (honoring `Retry-After`) and routing to the next — so one key hitting its limit
doesn't stall the pipeline:

```json
{
  "Llm": {
    "Claude":      { "ApiKeys": ["sk-ant-a", "sk-ant-b", "sk-ant-c"] },
    "AzureOpenAI": { "ApiKeys": ["key-east", "key-west"] }
  }
}
```

The pool combines `ApiKeys` with the single `ApiKey` and any runtime Settings override (deduped).
Azure keys in a pool share the one configured endpoint.

### Optional: persistence

Provide a connection string to enable EF Core / Postgres (otherwise the app runs stateless):

```bash
docker compose up -d
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=agentic_sdlc;Username=postgres;Password=postgres"
```

## API

All `/pipeline*`, `/requirement`, `/code`, `/test`, `/qa`, `/runs*`, and `/settings*` endpoints
require a JWT bearer token; `POST /auth/token` exchanges operator credentials for one. `/health`
and `/` are public.

| Method | Path | Description |
|---|---|---|
| `POST` | `/auth/token` | Exchange credentials for a JWT bearer token |
| `POST` | `/requirement` | Run the Requirement agent on its own |
| `POST` | `/code` | Run the Coding agent on its own |
| `POST` | `/test` | Run the Testing agent on its own |
| `POST` | `/qa` | Run the QA agent on its own |
| `POST` | `/pipeline` | Run the full end-to-end flow |
| `POST` | `/pipeline/stream` | Run the pipeline, streaming progress over SSE |
| `GET`  | `/runs`, `/runs/{id}` | List / fetch persisted runs |
| `GET`  | `/health` | Health check (public) |

## Deploy

```bash
azd up   # provisions + deploys to Azure Container Apps via the Aspire AppHost
```

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for the full deployment guide and
[docs/SETUP.md](docs/SETUP.md) for local setup, secrets, and CI.

## Contributing

Issues and PRs are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for the dev workflow and
conventions, and [SECURITY.md](SECURITY.md) for reporting vulnerabilities. Build and test with
`dotnet test AgenticSdlc.sln -c Release`; CI runs the same on every push and PR. Commits follow
[Conventional Commits](https://www.conventionalcommits.org/); `Nullable` and `TreatWarningsAsErrors`
are enabled across the solution.

## License

[MIT](LICENSE)
