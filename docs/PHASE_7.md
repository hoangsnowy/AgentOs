# Phase 7 — Agent Studio (Blazor Server, realtime UI + orchestration editor)

> Status: ✅ Build + tests green (.NET 10.0.202, 154 pass / 4 skip live-smoke), live verify in the browser OK.
> Quality Loop demo correct: QA fails iteration 1 (0.62) → passes iteration 2 (0.92), 5 agents shown in realtime.

## Objectives

Add a realtime presentation layer to the prototype: a Blazor Server UI that lets you enter a user story,
click run, and **watch the 5 agents collaborate in real time** (agent timeline + QA iterations + scores),
along with a metrics panel (token / cost / latency) and a tab to view the generated artefacts (Requirement / Code / Test / QA).

This directly serves **Section 2.5** (the experimental scenario: input → output → visible evaluation) and
**Section 2.6** (effectiveness metrics), and is a visual demo tool for the defense (showing the
Leader–Specialists–Quality Loop pattern running live).

## Plan (agreed)

- Host: **Blazor Server** (Blazor Web App, InteractiveServer render mode). The circuit runs over SignalR
  ⇒ realtime push "for free", with no need to write a Hub by hand.
- Integration: the Web project **references Infrastructure directly**, calling `PipelineOrchestrator` directly.
- Realtime mechanism: the orchestrator emits progress events; the component listens and then calls `StateHasChanged()`.

## Deliverables

### Core (keeping TreatWarningsAsErrors=true)

- `Domain/Pipeline/PipelineProgressEvent.cs` — a progress event record + enums `PipelineStage`, `PipelinePhase`.
- `Application/Pipeline/IPipelineProgressSink.cs` — the progress-emission port (abstraction).
- `Infrastructure/Pipeline/NullPipelineProgressSink.cs` — a no-op implementation, registered by default in `AddAgents`
  (via `TryAddSingleton`) ⇒ the API + the 80 existing tests have UNCHANGED behavior.
- `Infrastructure/Orchestration/PipelineOrchestrator.cs` — adds an **optional** ctor parameter
  `IPipelineProgressSink? progress = null` (non-breaking with the 4 test call-sites using `new PipelineOrchestrator(...6 args...)`).
  Inserts `ReportAsync` at: Requirement start/done, each Coding/Testing/QA iteration start/done, QA-completed (with score),
  Aggregate, and the Failed branches.

### Presentation project `src/AgenticSdlc.Web` (TreatWarningsAsErrors=false)

- `AgenticSdlc.Web.csproj`, `Program.cs`, `appsettings.json`, `Properties/launchSettings.json` (port 5180).
- `Components/`: `App.razor`, `Routes.razor`, `_Imports.razor`, `Layout/MainLayout.razor`,
  `Pages/PipelineStudio.razor` (the main page, route `/`).
- `wwwroot/app.css` (dark theme).
- `Services/CircuitPipelineProgress.cs` — an `IPipelineProgressSink` scoped per circuit; forwards events to
  the `Listener` that the component registers.
- `Services/CodeHighlighter.cs` — lightweight C# syntax highlighting, XSS-safe (HTML-encode first, single regex pass).
- `Services/Demo/DemoRunContext.cs` — a `UseDemo` flag per circuit.
- `Services/Demo/DemoLlmClient.cs` — an offline "canned" LLM source: recognises the agent via the system prompt
  ("You are the … Agent"), returns valid JSON per schema, **simulates QA fail iteration 1 → pass iteration 2** (configured via
  `Demo:FailingQaRounds`, `Demo:StepDelayMs`).
- `Services/Demo/DemoAwareLlmClientFactory.cs` — overrides `ILlmClientFactory`: `UseDemo` ⇒ DemoLlmClient,
  otherwise delegates to the real `LlmClientFactory` (Claude/Azure per appsettings).

### Solution

- `AgenticSdlc.sln` — added the `AgenticSdlc.Web` project (GUID `...006`) to the `src` solution folder.

## Technical decisions

- **Why not write a dedicated SignalR Hub?** Blazor Server already has a circuit (SignalR); using a
  scoped `IPipelineProgressSink` + `InvokeAsync(StateHasChanged)` is realtime enough, less code, and idiomatic.
  (A broadcast Hub is only needed later if multiple people watch one run together.)
- **Why a DemoLlmClient instead of Mock fixtures?** `MockLlmClient` is hash-based; a miss ⇒ "stub-response"
  (not JSON) ⇒ the agent fails to parse. Fixtures are also brittle (see Phase 5). DemoLlmClient returns JSON that matches the
  schema, is deterministic, and illustrates the Quality Loop — running offline with no API key.
- **Why is the progress parameter optional (= null → NullPipelineProgressSink.Instance)?** So the 4 test call-sites
  `new PipelineOrchestrator(...)` with 6 parameters still compile; DI (API/Web) still injects the registered sink.
- **Switching the LLM source at runtime:** the page resolves `IOrchestratorAgent` from the circuit's `IServiceProvider`
  AFTER setting `DemoRunContext.UseDemo` (because the agent reads `factory.Create` in its constructor).

## How to run

```bash
# at D:\LuanVan\prototype
dotnet build AgenticSdlc.sln -c Release
dotnet test  AgenticSdlc.sln -c Release          # expected: the 80 existing tests still green
dotnet run --project src/AgenticSdlc.Web         # open http://localhost:5180
```

Enter a user story → click "Run pipeline" (offline Demo mode by default) → observe the realtime timeline.

## Remaining work (TODO for a later session)

- [x] Build + test confirmation; fix minor syntax/analyzer errors if any (the Web project has TWAE off, so low risk).
- [x] Tick the Phase 7 checkbox in the `README.md` "Roadmap" section after the build is green.

> The optional items (export, test interpreter, …) have been moved to the **Backlog** section below.

### Bug fixed during live verify

`DemoLlmClient` identified the agent via `Contains(sys, "Testing Agent")` — but the Testing Agent's system prompt
contains the phrase *"…for the code produced by the **Coding Agent**…"*, and the `"Coding Agent"` branch
was checked FIRST ⇒ the Testing call was misrouted to `CodeJson` (the code-artifact shape, missing
`happyPathCount/edgeCaseCount/errorCaseCount`) ⇒ it failed the `test-artifact.v1` schema, breaking the pipeline at the Testing step.
Fix: match on the full identifier line `"You are the <X> Agent"` (as intended per the file header) instead of the bare suffix.

## Phase 7b — Orchestration Studio (Synapse-style drag-and-drop editor)

> Status: ✅ Build + tests green (154 pass), live verify in the browser OK.

Adds a visual node-graph editor (inspired by Synapse) in place of the linear timeline:
a dark canvas, a card per step, route-labelled edges, a minimap, and the QA loop drawn as a cycle.

- **Routes**: `/` = Orchestration Studio (editor); `/timeline` = the old realtime view (kept as-is).
- **New dependency**: `Z.Blazor.Diagrams` 3.0.4.1 (a pure-Blazor node-editor lib, MIT) — only in the Web project.
- **Models** (`Orchestrations/`): `OrchestrationGraph` / `GraphNode` / `GraphEdge` / `StepType`;
  `OrchestrationStore` (singleton, seeds + persists to `App_Data/orchestrations.json`); `StepNodeModel : NodeModel`.
- **Seeds**: `5-Agent SDLC Pipeline` (maps to KC1–KC5, Run works) + `Strict Developer` (replicates the Synapse screenshot).
- **Editor**: an "Add step" palette (14 types) → add nodes; drag to move; connect ports to draw edges; an inspector edits
  title/role/in/out/max/route; Save / New / Duplicate / Delete; a selector to switch orchestration; zoom + fit.
- **Run** (`OrchestrationStudio.Run`): interprets the graph — starting from the Start node along the edges, one LLM turn per node
  (offline Demo), lighting up nodes in realtime + streaming the Run Log. The evaluator branches on `isConsistent` ⇒ illustrating the
  QA loop (fail iteration 1 → loop → pass iteration 2). Models are assigned by role ⇒ the estimated cost matches the hybrid LLM.
- **Decision**: use `Z.Blazor.Diagrams` (node = Razor component) rather than embedding React/Svelte Flow — keeping everything
  in C#/Blazor, with easy realtime binding; the canvas is recreated via `@key="_graph.Id"` when switching orchestration.

## Backlog — future development (OUT of current scope)

The items below are intentionally left as **stubs / decoration** in this version (enough to illustrate the thesis). Recorded for later expansion:

### UI currently decorative only
- [ ] **18-item sidebar** (General, Build Agents, MCP Servers, Tool Builder, Repos, DB Configs, Models,
  Messaging, Integrations, Schedules, Vault, Usage, Import/Export, Logs, Memory, API Keys, Support & Docs) —
  currently only "Orchestrations" works; the rest do nothing when clicked.
- [ ] **Build with AI** — disabled button. Idea: enter a description → the LLM generates the orchestration graph automatically.
- [ ] **Deploy as Agent** — disabled button. Idea: export the orchestration as a runnable endpoint/agent.

### Run / execution (the most worthwhile for the thesis)
- [ ] **Run with a real LLM** — add a Demo/Real toggle like the `/timeline` page (the orchestration currently only runs offline Demo).
- [ ] **Recent Runs history** — each Run stores token/cost/latency/timestamp; a tab to review + compare.
- [ ] **Guardrails enforcement** — currently only displays text; does not yet block/check at Run time.

### Advanced node semantics (currently run as a generic LLM)
- [ ] **Tool** calls a real tool/function · **Parallel** actually forks branches · **Merge** merges · **Transform** maps data ·
  **Extract JSON** · **Switch / If-Else** branches on real conditions · **Print** logs values.

### Other
- [ ] An **export results** button to `.md/.json` for the appendix.
- [ ] **Tests** for `PipelineOrchestrator` event ordering + for the `OrchestrationStudio.Run` interpreter.

## Cleanup note

While generating code, one file was mistakenly written to `OneDrive\...\Documents\Claude\Projects\LuanVan\prototype\src\...`
(not the real repo). The real repo is at `D:\LuanVan`. Delete that stray directory if it still exists.
