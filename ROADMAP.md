# AgentOS — Roadmap

> Where AgentOS is today and where it's going. AgentOS is a .NET-native, multi-tenant
> operating shell for AI software-engineering agents. Horizon: ~18 months.

## Contents

1. [Vision & positioning](#1--vision--positioning)
2. [Where it is today](#2--where-it-is-today)
3. [Roadmap](#3--roadmap)
4. [Technical pillars](#4--technical-pillars)
5. [Business model](#5--business-model)
6. [Risks & mitigations](#6--risks--mitigations)
7. [Definition of success](#7--definition-of-success)

---

## 1 — Vision & positioning

**AgentOS is a desktop-native, multi-tenant operating shell for AI software-engineering agents.**
Most agent frameworks are Python libraries you wire together in code. AgentOS is a running
*product*: a Blazor desktop environment on a .NET 10 backend that coordinates a team of
role-specialized agents — Orchestrator, Requirement, Coding, Testing, QA — across the software
development lifecycle, with a provider-agnostic LLM gateway, enterprise identity, per-tenant
governance, and evidence/audit built in.

### Competitive positioning

| Product | Form factor | Language | Focus | Multi-tenant / on-prem | Where AgentOS differs |
|---------|-------------|----------|-------|------------------------|------------------------|
| [AutoGen](https://github.com/microsoft/autogen) (Microsoft) | Python library | Python | General multi-agent conversation | No (library) | AgentOS is a deployed product with UI + identity, not a lib you assemble |
| [MetaGPT](https://github.com/geekan/MetaGPT) | Python framework | Python | "Software company" role agents | No | MetaGPT ends at code-gen scripts; AgentOS adds tenancy, audit, governance, UI shell |
| [CrewAI](https://github.com/crewAIInc/crewAI) | Python framework | Python | Role/crew orchestration | Partial (Enterprise SaaS) | .NET-native, on-prem-first, SDLC-specialized vs generic crews |
| [Devin](https://www.cognition.ai/) (Cognition) | Hosted SaaS | Closed | Autonomous SWE agent | Cloud only, closed | AgentOS is open, self-hostable, inspectable; Devin is a black box |
| [Cursor](https://cursor.com/) | IDE (VS Code fork) | Closed | In-editor coding assist | No (per-dev) | Cursor = single-dev IDE; AgentOS = team/tenant SDLC platform |

**The gap AgentOS fills:** every popular framework is either (a) a Python library where you're the
integrator, or (b) a closed SaaS. Nobody offers a **.NET-native, desktop-shell, multi-tenant,
on-prem-ready SDLC agent platform with governance and evidence built in.** That's a defensible niche
for organisations that (1) live in the Microsoft/.NET stack, (2) can't ship code to a third-party
cloud (finance, gov, healthcare), and (3) want to *govern and measure* agent output, not trust it blindly.

### Target personas

1. **Enterprise dev team** in a .NET shop — wants agent assistance but code can't leave the building. On-prem + Keycloak/Entra SSO is the unlock.
2. **Software consultancy** — many client projects, needs hard tenant isolation so client A never sees client B's code or prompts. Multi-tenant is the unlock.
3. **Internal platform / DevEx team** — wants to offer "agents as a service" to its own engineers with audit, cost control, and RBAC. Open-core + observability is the unlock.

---

## 2 — Where it is today

AgentOS is a working platform, not a sketch. Already shipped:

- **5-agent SDLC pipeline** (Orchestrator/Requirement/Coding/Testing/QA) in a **Leader–Specialists–Quality loop** that iterates to convergence or an iteration cap.
- **Provider-agnostic LLM gateway** — Claude + Azure OpenAI via `Microsoft.Extensions.AI` `IChatClient`, plus Microsoft Agent Framework and a paired dev-machine **RemoteAgent** (zero server API tokens). Multi-key pool with 429 failover; cost calculation.
- **Modular monolith** — each feature is a self-contained `IModule` with its own DI surface, EF Core context, and Postgres schema; any module can later ship as a standalone package.
- **Multi-tenant identity** — Keycloak OIDC (RS256) or operator mode (HS256), tenant claim, **row-level isolation** via an EF global query filter.
- **Governance & evidence** — every tool call flows through `IToolGateway` → per-tenant `IToolPolicy` → `IToolInvocationLog`. Per-tenant **rate limiting**, a month-to-date **budget gate**, and a **`runner_shell` output cap** are live.
- **Tools & MCP** — GitHub PR service, build verifier, an MCP client (consume external tool servers), and AgentOS's own pipeline served as MCP tools.
- **Remote repo execution** — Workspaces + Sessions drive boards → tickets → AI coding sessions against connected repos.
- **Plugin system** — runtime-discovered `IAgentOsPlugin` extensions dropped into a `plugins/` folder, no compile-time reference.
- **Supply-chain + SAST** — Dependabot, dependency-review, CodeQL on every push/PR.

---

## 3 — Roadmap

Product milestones (not a fixed calendar). Each: goal · deliverable · signal.

### M1 — OSS polish
- **Goal:** a credible, easy-to-adopt OSS project.
- **Deliverable:** sharp README with architecture diagram + screenshots; runnable examples; first-party sample plugin; a few engineering write-ups (why .NET agents, multi-tenant agent isolation, the LLM gateway, the on-prem story).
- **Signal:** external issues + the first non-author PR merged.

### M2 — Extensibility depth
- **Goal:** go beyond the fixed 5 agents.
- **Deliverable:** richer plugin contracts (agent plugins, prompt-template packs, BYO LLM provider adapters); a `plugin-scaffold` codegen path for third parties; a minimal registry/discovery; first-party sample plugins.
- **Signal:** external contributors; community plugins published.

### M3 — Enterprise-ready
- **Goal:** buyable by an enterprise.
- **Deliverable:** SSO beyond Keycloak (Okta, Microsoft Entra ID); fine-grained RBAC (per-project, per-agent, per-action); full audit log + export; SOC 2 readiness checklist + evidence; on-prem deploy guide (Docker Compose + Helm/k8s + `azd`).
- **Signal:** a signed enterprise pilot (design-partner or paid).

### M4 — Observability + AIOps
- **Goal:** cheap, self-improving agent ops.
- **Deliverable:** OpenTelemetry GenAI semantic conventions (a span per LLM call with model/tokens/cost); a cost-optimization dashboard (spend by agent/model/tenant/day — the `cost-report` skill is the seed); productized prompt tuning (`prompt-tune`); prompt-drift regression alerts.
- **Signal:** a measured cost reduction vs a naive "always call the biggest model" baseline (routing + caching + cheaper-model fallback).

### M5 — Ecosystem
- **Goal:** a self-sustaining ecosystem.
- **Deliverable:** an agent / prompt-pack marketplace with quality gating; certified, benchmarked packs; a community contribution flow.
- **Signal:** marketplace items in real use; first revenue (sponsor, paid pack, or hosted seat).

---

## 4 — Technical pillars

Long-term architecture commitments — where we are → where we go.

- **Orchestration** — *Now:* 5 agents in `AgentOs.Modules.Pipeline` (+ optional MAF workflow engine). *Next:* user-defined agents via plugins; an orchestration graph (DAG, conditional routing, parallel fan-out); no lock-in in the orchestration layer (our contracts, MAF/SK as one engine, not the only one).
- **LLM abstraction** — *Now:* Claude + Azure OpenAI + MAF + RemoteAgent behind `ILlmClient`/`ILlmClientFactory`. *Next:* **local models** (Ollama, vLLM) for fully air-gapped on-prem; capability negotiation (tool-calling/vision/context window) so the router picks per task. Rule: **always ≥2 providers wired.**
- **State** — *Now:* per-module Postgres contexts (modular monolith). *Next:* event-sourced agent runs → replay + time-travel debugging; snapshot a run, branch it, re-run with a different prompt/model (doubles as the prompt-drift regression harness).
- **Observability** — *Now:* structured logs (enough to drive `cost-report`). *Next:* OpenTelemetry GenAI spans; exemplar traces; cost telemetry as a first-class signal, not a log scrape.
- **Security** — *Now:* Keycloak OIDC, JWT, tenant row-level isolation, per-tenant rate limit + budget gate + `runner_shell` cap, CodeQL SAST. *Next:* **sandboxed code execution** — the Coding/Testing agents currently build in a temp dir; jailing that in a no-network/no-secret container is the single biggest gap before any "agent runs your code" feature hardens. Plus zero-trust between modules, prompt-injection defense, and output filtering (PII/secrets).
- **Multi-tenant** — *Now:* row-level isolation via tenant context. *Next:* tiered isolation — row-level → schema-per-tenant → isolated infra per tenant; sell isolation level as a pricing axis.
- **Knowledge** — *Now:* agents are stateless beyond prompt context. *Next:* RAG over the tenant's own codebase + docs (pgvector, stays in Postgres); citation tracking so a claim links back to a source line.
- **Tools** — *Now:* MCP client + server, GitHub PR service, build verifier, `runner_shell`. *Next:* broader MCP tool catalog, browser automation for agents that verify a running app, richer native API calls.
- **UI** — *Now:* Blazor desktop shell, interactive circuits. *Next:* SSR + interactive islands for faster first paint; a mobile read-only view (check a run from your phone); a **CLI** for CI/headless use — same backend, no browser.

---

## 5 — Business model

| Model | Free | Paid | Pros | Cons |
|-------|------|------|------|------|
| **OSS-first** (sponsor) | Everything | Goodwill | Max adoption | Revenue weak/unpredictable for a small team |
| **Open core** | Core agents, single-tenant, basic auth | SSO, fine-grained RBAC, audit/compliance, schema/infra isolation, support | Adoption *and* a clear paid line | Must police the core/enterprise boundary |
| **SaaS** | Trial | Hosted seats | Recurring revenue | On-prem buyers (the whole point) can't use it |
| **Consulting** | The OSS | Time | Immediate cash + user insight | Doesn't scale |

**Recommendation — open core.** The target personas (on-prem .NET enterprises, consultancies)
*cannot* use SaaS — keeping code in-house is exactly why they'd pick AgentOS, which rules out pure
SaaS as the primary model. The features enterprises pay for (SSO/Entra, fine-grained RBAC,
audit/SOC 2, infra-level isolation, priority support) are precisely the ones that should be gated —
and they're the M3 roadmap. Start OSS for adoption, introduce the enterprise edition when those
features land. Consulting fills the gap and feeds requirements. A hosted "AgentOS Cloud" stays a
*later optional* offering, not the core bet.

---

## 6 — Risks & mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **LLM cost explosion** | A runaway loop burns the budget in hours | ✅ per-tenant/per-run budget gate + rate limit (shipped); cheaper-model routing (M4); cache identical prompts |
| **Agent loop / hallucination** | Confidently wrong code, infinite loops | ✅ QA gate + max-iteration cap (shipped); human-in-the-loop checkpoints; a quality benchmark in CI |
| **Multi-tenant data leak** | Tenant A sees tenant B → trust dead, legal exposure | ✅ row-level isolation via EF global query filter + tenant-isolation tests; audit the tenant context on every data-access PR; tiered isolation for high-assurance tenants |
| **Prompt drift** | Yesterday's good prompt silently regresses | Version prompts v1→vN; snapshot fixtures; regression test in CI (`prompt-tune` scores variants) |
| **Vendor lock-in** | A provider changes pricing/API | ✅ `IChatClient` abstraction + ≥2 providers wired; add a local model (Ollama/vLLM) as the always-available floor |
| **Prompt injection** | Malicious input hijacks an agent | Strict template boundaries (untrusted input never concatenated into the system prompt); input/output classification; secret/PII output filter |
| **Sandbox escape** | Agent-run code touches the host | Never execute generated code on the host; jailed container with no network/secret access — a hard dependency before any "agent runs your tests" feature ships |

---

## 7 — Definition of success

- **Near term** — OSS adoption: external PRs merged, issues from non-authors, a growing contributor base.
- **Mid term** — one enterprise pilot in production; a published plugin ecosystem; a proven cost story vs a naive baseline.
- **Long term** — pick the lane the data points to: a thriving open-core/community project (companies in production, sustainable contributors) **or** a commercial lane (enterprise seats + hosted AgentOS Cloud). Instrument adoption months in; let the numbers choose rather than pre-committing.
