# Roadmap: from SDLC pipeline → open-source Agentic-SDLC framework

> Long-term direction for evolving this repository from a fixed SDLC pipeline into a general,
> governance-first agentic framework. Status: approved direction (2026-05-27). Horizon 0 stabilizes
> the current core toward a `v1.0` tag; Horizons 1–2 build the platform on top.

## Why this exists

The current prototype is an **application**, not yet a **platform**: a sequential pipeline of 5 fixed
agents where each agent is one chat-completion call + JSON parse, run in-process. It abstracts exactly
**one** axis — the model provider (`ILlmClient`). A platform needs to abstract the other axes too:
agent runtime, tools, memory/cognition, governance, execution evidence, and durable workflow. This
roadmap turns that gap into an ordered path.

## Locked strategic decisions

| Axis | Decision | Roadmap consequence |
|---|---|---|
| North star | **Open-source framework** | Prioritize clean public API, docs, extensibility; the SDLC pipeline becomes the flagship example |
| Architecture | **Hybrid** | Keep the .NET core; **build** the novel parts (IAgentRuntime, governance, evidence); **reuse** Semantic Kernel (agent loop) + MCP (tools) |
| Maturity | **Stabilize the core first** | Horizon 0 (stabilization) is in scope |
| First capability | **Real tool execution** | After the runtime layer, build tools+sandbox+governance before memory/workflow |

## Hybrid principle — build only what is differentiating

| Build (our value) | Reuse (don't reinvent) |
|---|---|
| `IAgentRuntime` contract (separate orchestration from how an agent executes) | **Semantic Kernel** — agent loop, function-calling, planners for .NET |
| Governance / approval gate (gate risky/irreversible actions) | **MCP (Model Context Protocol)** — tool standard + ecosystem (fs, git, shell, build/test) |
| Evidence / lineage store (audit, explainability) | **OpenTelemetry** (already wired via Aspire) — tracing |
| Capability model + sandbox policy | **EF Core / Postgres** (already present) — persistence; **Aspire** — orchestration/observability |
| SDLC-specific orchestration (Leader-Specialists-Quality-Loop) | Durable workflow engine (evaluate: Microsoft DurableTask / Dapr Workflows / Temporal .NET) |

**Differentiator:** a **.NET-native, governance-first** agentic framework with built-in evidence/lineage,
MCP tools, a **visual Agent Studio** (Web), and an SDLC reference pipeline — distinct from the Python-centric,
mostly-headless LangGraph / AutoGen.

---

## Horizon 0 — Stabilization (now → v1.0)

Stabilize the existing core before building new layers. No large refactors yet.

- **H0.1 (top priority) — stable benchmark numbers.** The committed benchmark harness runs on
  `MockLlmClient` (deterministic, 123 ms) and cannot produce real-LLM figures. Run a live `n=10`
  benchmark in the true hybrid config and preserve the raw artifacts/logs. See
  **[KC_REPRODUCIBILITY.md](KC_REPRODUCIBILITY.md)**.
- **H0.2 — demo prep:** verify the Blazor offline Demo end-to-end; optionally record one live run as a fallback.
- **H0.3 — tag v1.0:** once the core is stable, `git tag v1.0` as the reference-app baseline.

## Horizon 1 — platform-v2 foundation (post-defense, ~3–6 months)

Ordered by causal dependency, tuned to "tool execution first".

- **H1.0 — repo strategy.** Pin the SDLC pipeline as the reference app (`v1.0`); start the framework
  in a new public module/repo. Choose a name + license (Apache-2.0 or MIT).
- **H1.1 — `IAgentRuntime` (W1).** The root abstraction separating *orchestration* from *how an agent
  executes*. Two impls to start: (a) `InProcessLlmRuntime` (wraps today's single-shot `ILlmClient` — a
  compatibility path); (b) `SemanticKernelRuntime` (SK agent + think→tool→observe loop). The hybrid payoff.
  Later: `ClaudeCodeCliRuntime` (out-of-process).
- **H1.2 — tool execution + sandbox + capability + governance gate (W3 + W7).** *(first capability)* Tools
  via **MCP** (filesystem, git, shell, `dotnet build`/`test`). Every tool call passes capability check →
  sandbox (process/container isolation) → governance gate (auto-approve / require human for irreversible
  actions). Turns "guess the code" into "evidence: built it, here is the output".
  Security is a P0 here. The approval gate surfaces in the **Agent Studio** (Web): a human approves/denies a
  pending risky action from the UI — this is where Human-in-the-Loop becomes a real mechanism, not a principle.
- **H1.3 — persistent cognition + evidence/lineage (W2 + W8).** Memory store as a separate bounded context
  (episodic / semantic-vector / lineage / decision-ADR). Evidence store: each step leaves a reproducible
  record (real prompt, raw output, tool I/O, why QA failed). Basis for audit, learning, and OSS explainability.
  The **Agent Studio** gains an evidence/lineage viewer: replay each agent step with its real prompt, raw output
  and tool I/O — the visual counterpart to the evidence store.
- **H1.4 — durable workflow (W4).** Replace the in-process `for` loop with a checkpoint/replay engine (runs
  survive restarts, resume, distribute).
- **H1.5 — broaden providers/runtimes (W5).** Add Ollama (self-host), OpenRouter, Claude Code CLI — now the
  abstraction is ready to plug them in without breaking.

## Horizon 2 — OSS framework maturity (~6–18 months)

- **H2.1 — prompt registry (W6):** prompts as versioned runtime resources (file/DB), A/B-able, not compiled in.
- **H2.2 — control-plane / data-plane split (W9):** thin control-plane + independently scaled execution workers.
- **H2.3 — capability-based restructure (W10):** Runtime / Tools / Memory / Governance / Workflow / Providers —
  as a *result* of H1, not empty folders up front.
- **H2.4 — full-SDLC agents:** Design / DevOps / Documentation / Maintenance agents,
  plugged into the existing Leader-Specialists protocol.
- **H2.5 — research outputs:** a multi-agent SDLC benchmark (extend HumanEval / SWE-bench to
  multi-agent flows); an automated adversarial-testing harness (PyRIT/Garak-style for the pipeline); optional
  RLHF / fine-tuning. Strong OSS differentiators.
- **H2.6 — Agent Studio (Web) as the flagship UI.** Grow the Phase-7 Blazor "Agent Studio" from an offline demo
  into the framework's visual front-end: a drag-and-drop orchestration editor (agents / tools / runtimes as nodes
  — the Phase-7b "Build with AI" + advanced-node backlog), an MCP-server management panel, the governance approval
  queue (H1.2), the evidence/lineage viewer (H1.3) and run history. Decouple it from the in-process engine to talk
  to the control-plane API (couples with H2.2). A **visual Studio is a key OSS differentiator** vs headless Python
  frameworks — incrementally lit up by H1 capabilities, not a rewrite.

## Weakness index (source of the ordering)

| ID | Gap | Horizon |
|---|---|---|
| W1 | No `IAgentRuntime` (only chat-completion) | H1.1 |
| W2 | Stateless agents — no persistent cognition | H1.3 |
| W3 | No tool-calling, no sandbox/capability | H1.2 |
| W4 | Hard pipeline, not a durable/resumable workflow | H1.4 |
| W5 | Narrow provider set; "agnostic" only at the chat-model layer | H1.5 |
| W6 | Prompts are `static`, compiled into the app | H2.1 |
| W7 | No governance: policy & approval gates are principles, not mechanisms | H1.2 |
| W8 | Tracing stops at the service layer — no reasoning-level evidence | H1.3 |
| W9 | Both hosts run the engine in-process — no control/data-plane split | H2.2 |
| W10 | Folder layout reflects a 3-layer app, not a capability platform | H2.3 |

## OSS-specific work (cross-cutting)

- **Repo split:** pinned `prototype` (tag `v1.0`, reference app) ↔ new public framework repo.
- **Public API surface:** stable contracts (`IAgentRuntime`, `ITool`, `IMemoryStore`, `IWorkflow`, provider
  plugin model) + semantic versioning + NuGet packaging.
- **Docs site (make-or-break for OSS):** getting-started · concepts · "extend your own agent/tool/runtime/provider"
  · examples (the SDLC pipeline as flagship).
- **Agent Studio (Web):** ship the Blazor Studio as the framework's visual front-end (orchestration editor,
  governance approvals, evidence/lineage viewer, run history) — a differentiator most agent frameworks lack. See H2.6.
- **Contribution model:** CONTRIBUTING, issue/PR templates, CI for PRs, samples.

## Verification per horizon

- **H0:** `dotnet build` + `dotnet test` green on a real machine; a live benchmark run produces a stable
  report; the Blazor offline demo runs end-to-end.
- **H1:** each capability ships with tests + a runnable demo (e.g. an integration test where the Coding Agent
  runs `dotnet build` via MCP in a sandbox and QA reads the real compiler output; a test proving a risky action
  blocks pending approval).
- **H2:** the benchmark reproduces; the docs site builds; the sample app runs from a fresh clone; the NuGet
  package installs into an empty project.
