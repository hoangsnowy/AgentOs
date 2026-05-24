# agentic-sdlc-net

> Reference prototype for a **multi-agent AI** model supporting the software development lifecycle (SDLC), built on **.NET 10** and **Microsoft Azure**, using a **hybrid LLM** architecture (Claude — Anthropic API + GPT — Azure OpenAI Service).

This is the companion product to the Master's thesis **"Research and Application of the Agentic AI Model in the Software Development Lifecycle (SDLC)"** — Nguyen Minh Hoang, Hanoi University of Business and Technology, 2026.

---

## Objectives

The prototype realises the **Leader-Specialists-Quality Loop** architecture with 5 agents:

| Agent | Role | Default LLM |
|---|---|---|
| **Orchestrator Agent** | Central coordination, task assignment, aggregation | Claude Haiku 4.5 |
| **Requirement Agent** | Requirement analysis → Structured Requirements JSON | Claude Sonnet 4 |
| **Coding Agent** | Generate C# scaffold code following Clean Architecture | GPT-4.1 (Azure OpenAI) |
| **Testing Agent** | Generate xUnit test cases (happy / edge / error) | GPT-4o-mini (Azure OpenAI) |
| **QA Agent** | Assess requirement-code-test consistency, max 3 iterations | Claude Haiku 4.5 |

Assigning an LLM to each agent is configurable via `appsettings.json` — a *Platform Agnostic* architecture.

---

## Architecture

The solution contains 5 projects, organised following Clean Architecture:

```
agentic-sdlc-net/
├── src/
│   ├── AgenticSdlc.Domain/         # Entities, value objects (RequirementSpec, CodeArtifact, ...)
│   ├── AgenticSdlc.Application/    # Agent interfaces (IRequirementAgent, ...)
│   ├── AgenticSdlc.Infrastructure/ # LLM Gateway (ClaudeClient, AzureOpenAiClient), agent impls
│   └── AgenticSdlc.Api/            # ASP.NET Core minimal API host
└── tests/
    └── AgenticSdlc.Tests/          # xUnit unit + integration tests
```

The LLM Gateway exposes the `ILlmClient` interface with 2 parallel implementations (`ClaudeClient`, `AzureOpenAiClient`) — registered via DI. Each agent receives an `ILlmClient` (already selected by the factory for its role) instead of calling a vendor SDK directly.

---

## Environment requirements

- **.NET 10 SDK** (LTS, released 11/2025).
- One of (or both) LLM accounts:
  - **Anthropic API key** — create at <https://console.anthropic.com>
  - **Azure OpenAI Service** — create deployments for `gpt-4.1` and `gpt-4o-mini` via the Azure Portal
- (Optional) **Azure Cosmos DB** for persistence; by default the prototype uses an in-memory store.

Verify .NET 10:

```bash
dotnet --list-sdks
# Must contain a line starting with "10."
```

---

## Configuration

Copy `src/AgenticSdlc.Api/appsettings.json` to `appsettings.Development.json` (already in `.gitignore`) and fill in the secrets:

```json
{
  "Llm": {
    "Anthropic": {
      "ApiKey":   "sk-ant-...",
      "BaseUrl":  "https://api.anthropic.com",
      "Version":  "2023-06-01"
    },
    "AzureOpenAI": {
      "Endpoint": "https://<your-resource>.openai.azure.com",
      "ApiKey":   "<your-key>"
    }
  },
  "Agents": {
    "Orchestrator": { "Provider": "Anthropic",   "Model": "claude-haiku-4-5",   "Temperature": 0.3, "MaxTokens": 2000 },
    "Requirement":  { "Provider": "Anthropic",   "Model": "claude-sonnet-4",    "Temperature": 0.1, "MaxTokens": 2000 },
    "Coding":       { "Provider": "AzureOpenAI", "Model": "gpt-4.1",            "Temperature": 0.2, "MaxTokens": 4000 },
    "Testing":      { "Provider": "AzureOpenAI", "Model": "gpt-4o-mini",        "Temperature": 0.2, "MaxTokens": 3000 },
    "Qa":           { "Provider": "Anthropic",   "Model": "claude-haiku-4-5",   "Temperature": 0.1, "MaxTokens": 1500 }
  }
}
```

In a production or CI environment, use **Azure Key Vault** or **GitHub Actions Secrets** instead of a file.

---

## Build & Run

```bash
git clone https://github.com/<your-org-or-user>/agentic-sdlc-net.git
cd agentic-sdlc-net

# Restore + build
dotnet restore
dotnet build

# Run unit tests
dotnet test

# Run the API locally
dotnet run --project src/AgenticSdlc.Api
# Scalar UI:  http://localhost:5080/scalar/v1
# OpenAPI:    http://localhost:5080/openapi/v1.json
```

### End-to-end demo

```bash
curl -X POST http://localhost:5080/pipeline \
  -H "Content-Type: application/json" \
  -d '{"userStory":"The system needs a product management API that lets an admin create/view/edit/delete; users browse by category.","nMax":3}'
```

---

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/requirement` | Invoke the Requirement Agent on its own |
| `POST` | `/code` | Invoke the Coding Agent on its own |
| `POST` | `/test` | Invoke the Testing Agent on its own |
| `POST` | `/qa` | Invoke the QA Agent on its own |
| `POST` | `/pipeline` | Run the end-to-end flow (KC4 in the thesis) |
| `GET`  | `/health` | Healthcheck |

---

## Roadmap

- [x] Phase 1 — Solution skeleton, CI, README
- [x] Phase 2 — LLM Gateway (`ILlmClient` + 2 impls + factory + Mock)
- [x] Phase 3 — Domain models + 5 agent interfaces
- [x] Phase 4 — `PipelineOrchestrator` + endpoints
- [x] Phase 5 — Unit tests + benchmark KC1–KC5
- [x] Phase 6 — Azure deployment (Container Apps + App Insights)
- [x] Phase 7 — Agent Studio (Blazor Server, realtime UI + orchestration editor) — see [docs/PHASE_7.md](docs/PHASE_7.md)

---

## Thesis references

- Section 2.2 — Proposed Multi-Agent architecture
- Section 2.4 — Prototype implementation
- Section 2.5 — Experimental scenarios KC1–KC5

---

## License

MIT — see [LICENSE](./LICENSE).

---

## English summary

Reference prototype for a **multi-agent AI system** that supports the software development lifecycle (SDLC), built on **.NET 10** and **Microsoft Azure**, using a **hybrid LLM** strategy (Anthropic Claude + Azure OpenAI). The system orchestrates five specialised agents — Orchestrator, Requirement, Coding, Testing, QA — through a leader-specialists pattern with an explicit Quality Loop (max 3 iterations). Companion to the Master's thesis *"Research and Application of Agentic AI in the Software Development Lifecycle"* (Nguyen Minh Hoang, HUBT, 2026).
