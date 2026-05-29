# Phase 8 — Web / API production separation + auth

**Goal:** ship the system as a real client-server application, not a single-process prototype.
After Phase 8 the Web no longer owns the LLM gateway, the orchestrator, or the agents — it
becomes a thin Blazor client that drives a remote API. The API gets bearer-token auth so it
can be exposed publicly. LLM secrets become runtime-configurable from the Settings UI rather
than baked into `dotnet user-secrets`.

---

## Why this phase

Phases 1-7 produced a working prototype:

- API has REST endpoints for every agent and the full pipeline (`Api/Endpoints/PipelineEndpoints.cs`).
- Web has a Blazor desktop ("AgentOS") + Agent Studio.
- Both compose the same Infrastructure stack (`AddLlmGateway` + `AddAgents` + `AddPersistence`).

But the Web **never calls the API.** It registers the orchestrator + agents itself and runs
them inside its own Blazor circuit (`Web/Program.cs` Phase 7 baseline). Consequences:

| Symptom | Root cause |
| --- | --- |
| Cannot scale Web and API independently | They are the same process at runtime |
| Cannot front API with a load balancer | API has no real clients |
| Anyone POSTing `/pipeline` spends LLM tokens | No auth on any endpoint |
| Rotating an LLM key requires SSH + restart | Keys live in `dotnet user-secrets` only |
| Pipeline runs disappear on restart by default | Persistence is opt-in (no connection string → no-op repos) |

Phase 8 closes those gaps.

---

## Acceptance criteria

| # | Criterion | How verified |
| --- | --- | --- |
| 1 | Web has zero `IOrchestratorAgent` / `ICodingAgent` / etc. dependencies | `grep -r "IOrchestratorAgent" src/AgenticSdlc.Web` returns nothing |
| 2 | Web calls API over HTTP for every pipeline run | Wireshark / network panel shows `POST /pipeline/stream` to API base URL |
| 3 | API rejects unauthenticated `/pipeline*` requests with `401` | curl without bearer → `401`; curl with valid JWT → `200` |
| 4 | LLM keys can be rotated from the Settings UI without restart | Update key in UI, run pipeline, observe new key used (server log) |
| 5 | Postgres is required to start either host | Set `ConnectionStrings:DefaultConnection=""` → app fails fast at startup |
| 6 | KC1-KC5 live transcripts committed to `TestResults/kc_live/` | Path exists with `.csv` + `.json` + `summary.md` for `n=10` |
| 7 | `deploy.yml` runs to a staging slot and the smoke job passes | GitHub Actions UI shows green deploy + smoke |
| 8 | `docs/DEPLOY.md` archived; README phase table ticks Phase 8 | `git log` shows the doc moves |

---

## Sub-tasks

### 8.1 — Web → API over HTTP (SSE streaming)  ✅

### 8.2 — API JWT bearer auth                  ✅

### 8.3 — Web auth + LoginOverlay              ✅ (token in localStorage; cookie variant deferred)

### 8.4 — Runtime configuration store          🟡 (interface + in-memory + /settings endpoints; EF entity TODO)

### 8.5 — Postgres required by default         ✅

### 8.6 — KC live runner scripts               ✅ (live n=10 run still pending — needs real LLM creds)

### 8.7 — CI/CD deploy smoke                   ✅

### 8.8 — Docs sweep                           ✅

- New `Application.Pipeline.IPipelineClient` port (`StreamAsync(UserStory, CancellationToken)` →
  `IAsyncEnumerable<PipelineStreamEvent>`).
- New `Domain.Pipeline.PipelineStreamEvent` envelope (`Progress` | `Result` | `Error`).
- Two impls in `Infrastructure.Pipeline`:
  - `InProcessPipelineClient` — runs the orchestrator in the current process. Bridges
    `IPipelineProgressSink` → `Channel<>` → `IAsyncEnumerable`. Used by the API host and by the
    Web host in single-process dev mode.
  - `HttpPipelineClient` — POSTs to `{Api:BaseUrl}/pipeline/stream` and parses the SSE response
    (`event: progress|result|error\ndata: {json}\n\n`). Used by the Web host when `Api:BaseUrl`
    is set.
- New `MutableSinkHolder` scoped service in `Infrastructure.Pipeline`. Registered as the
  scope's `IPipelineProgressSink` so the orchestrator's progress reports flow through it.
  The endpoint (or the in-process client) swaps the inner sink at run time.
- New API endpoint `POST /pipeline/stream` in `Api/Endpoints/PipelineEndpoints.cs`. Writes
  SSE frames as the orchestrator emits progress, then a single terminating `result` or
  `error` frame.
- `PipelineStudio.razor` now injects `IPipelineClient` and consumes the stream with
  `await foreach`. The Phase 7 `CircuitPipelineProgress` is **deleted** (event source moved
  to the stream).
- DI extensions in `Infrastructure.Pipeline.PipelineClientExtensions`:
  `AddInProcessPipelineClient()` and `AddHttpPipelineClient(IConfiguration)`.
- Web `Program.cs` routes on configuration: `Api:BaseUrl` blank → in-process, set → HTTP.

**Deferred to 8.1b:** drop the orchestrator / agents / LLM gateway DI from the Web host
when `Api:BaseUrl` is set. Today both still register so single-process fallback works.

### 8.2 — API JWT bearer auth

- Add `AddAuthentication("Bearer")` + `AddAuthorization()` to the API.
- Mark every `/requirement`, `/code`, `/test`, `/qa`, `/pipeline`, `/pipeline/stream`,
  `/runs`, `/runs/{id}` endpoint with `RequireAuthorization()`.
- `/health` stays public; `/` (root) stays public.
- Configuration keys: `Auth:Bearer:Issuer`, `Auth:Bearer:Audience`, `Auth:Bearer:Secret`.
- Tests: unauthorized POST `/pipeline` → `401`; with bearer → `200`.

### 8.3 — Web auth + LoginOverlay

- New API endpoint `POST /auth/token` accepting `{ user, pass }` (compared against a
  bcrypt-hashed value in `app_config`) → returns `{ token, expiresAt }`.
- Web `LoginOverlay.razor` posts to it, stores the JWT in a HTTP-only cookie scoped to the
  circuit, and adds it as the bearer header for the `HttpPipelineClient` named HttpClient
  (via a `DelegatingHandler`).
- Logout = clear the cookie + redirect to `/`.
- Tests: end-to-end Web boot, login, run a mock pipeline, logout.

### 8.4 — DB-backed secrets

- New EF entity `app_config` (key TEXT, value BYTEA encrypted with DataProtection, updated_at TIMESTAMPTZ).
- New repo `IAppConfigRepository.GetAsync(key)` / `SetAsync(key, value)`.
- `LlmOptions` factory polls the repo on each LLM call (cache 15 s).
- `Settings.razor` page (in the Web) exposes the keys for the admin role:
  - Anthropic API key
  - Azure OpenAI endpoint + key
  - GitHub PAT
  - JWT signing secret (read-only display, rotatable via CLI)
- "Test connection" button calls the API `/llm/test` endpoint which makes a small probe call.
- Tests: write key → repo returns it; LLM factory refreshes within 15 s; non-admin POST `/settings`
  returns `403`.

### 8.5 — Postgres required

- Drop the no-op repo branch in `AddPersistence`. Require `ConnectionStrings:DefaultConnection`;
  fail fast at startup with a clear error.
- `docker-compose.yml` is the canonical local stack (already exists). README updates to:
  ```bash
  docker compose up -d
  dotnet run --project src/AgenticSdlc.Api
  dotnet run --project src/AgenticSdlc.Web
  ```
- Update `Tests/AgenticSdlc.Tests/Persistence/*` to require a real Postgres for the integration
  test class. CI gets a Postgres service container.

### 8.6 — KC live run + transcripts

- Add `tests/AgenticSdlc.Tests/Kc/LiveBench.cs` (`[Theory]` over the KC1-KC5 dataset).
- Skipped unless `KC_LIVE_MODE` env-var is set (`hybrid` / `claude` / `azure`).
- Writes `TestResults/kc_live/{mode}/{kc}_{run}.json` per call (raw transcript + tokens + cost
  + latency).
- Writes `TestResults/kc_live/{mode}/summary.md` aggregating Table 2.6 columns.
- One canonical run committed (n=10) before defense.

### 8.7 — CI/CD deploy.yml smoke

- `.github/workflows/deploy.yml` runs `dotnet publish` → `docker build` → `docker push` to ACR
  → `az containerapp update` for `agentic-sdlc-api` and `agentic-sdlc-web`.
- Smoke job after deploy: `curl https://api-staging.../health` (expects `200`) and
  `curl -X POST -H 'Authorization: Bearer $TOKEN' .../pipeline` with a smoke story (expects
  `200`).
- Gated on `main` branch only; PRs run build+test only.

### 8.8 — Docs sweep (this commit)

- `docs/PHASE_8.md` (this file).
- README phase table appends Phase 8 row (ticking 8.1, marking 8.2-8.7 in-progress).
- `docs/DEPLOY.md` gets a deprecation header pointing at `docs/DEPLOYMENT.md`.
- `docs/RUN_LIVE_PIPELINE.md` updated with the new `Api:BaseUrl` workflow.

---

## Run guide (single-host dev, Phase 8 layout)

```bash
# Local Postgres
docker compose up -d

# Terminal 1 — API (owns the orchestrator)
dotnet run --project src/AgenticSdlc.Api

# Terminal 2 — Web (talks to the API)
export Api__BaseUrl="http://localhost:5080/"     # Linux/macOS
$env:Api__BaseUrl = "http://localhost:5080/"     # PowerShell
dotnet run --project src/AgenticSdlc.Web
# → http://localhost:5180/
```

Single-process fallback (legacy Phase 7 mode): unset `Api:BaseUrl` and Web boots with its own
orchestrator. Useful for tests / Codespaces.

---

## Backout

If 8.1 misbehaves, revert via `git revert <commit>` — the SSE endpoint is additive and the
existing `POST /pipeline` keeps working. `PipelineStudio.razor` falls back to its Phase 7
listener pattern with a one-line edit (`@inject IPipelineClient Pipeline` → `@inject
IOrchestratorAgent Orchestrator` + the old `RunAsync`).

## Open questions

1. **Streaming transport.** SSE chosen for simplicity. Switch to gRPC bidi when the workflow
   editor (`OrchestrationStudio`) gets remote execution — gRPC bidi streams events both ways.
2. **Auth tier.** Single bearer token for "operator" today. Multi-tenant + per-tenant secret
   scoping is a Horizon 1 deliverable (`docs/architecture/MIGRATION_BACKLOG.md` M5).
3. **Live KC transcripts.** Anthropic + Azure OpenAI credentials required to run. Use the
   student tier on Azure OpenAI; Claude pay-as-you-go costs ~ $0.10 per `n=10` KC4 run.

