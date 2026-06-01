# Agent OS — Long-term Roadmap (12–18 months)

> Post-defense strategic plan for **Agent OS** (`agentic-sdlc-net`).
> From master's-thesis prototype (HUBT) to a real, demoable, potentially commercial product.
> Author: solo developer. Horizon: 18 months. Written: 2026-05-30.

---

## Table of Contents

1. [Vision & Positioning](#1--vision--positioning)
2. [Phase Roadmap (12–18 months)](#2--phase-roadmap-1218-months)
3. [Technical Pillars](#3--technical-pillars)
4. [Business Model Options](#4--business-model-options)
5. [Risk & Mitigation](#5--risk--mitigation)
6. [Quarter 1 Sprints (post-defense, 12 weeks)](#6--quarter-1-sprints-post-defense-12-weeks)
7. [Connection with the Thesis](#7--connection-with-the-thesis)
8. [Definition of Success (3 horizons)](#8--definition-of-success-3-horizons)

---

## 1 — Vision & Positioning

### Elevator pitch

**Agent OS is a desktop-native, multi-tenant operating shell for AI software-engineering agents.** Where most agent frameworks are Python libraries you wire together in code, Agent OS is a running *product*: a Blazor desktop environment (KDE Breeze styling) sitting on a .NET backend (`agentic-sdlc-net`) that orchestrates a team of role-specialized agents — Requirement, Coding, Testing, QA, Orchestrator — across the full software-development lifecycle. It ships with an LLM Gateway that abstracts providers (Claude, Azure OpenAI today), enterprise identity (Keycloak OIDC, multi-tenant), and a benchmark harness (KC1–KC5) that quantifies agent quality rather than hand-waving it. The thesis prototype proves the concept; this roadmap turns it into something a consultancy or internal platform team could actually run on-prem.

### Competitive positioning

| Product | Form factor | Language | Focus | Multi-tenant / on-prem | Where Agent OS differs |
|---------|-------------|----------|-------|------------------------|------------------------|
| [AutoGen](https://github.com/microsoft/autogen) (Microsoft) | Python library | Python | General multi-agent conversation | No (library) | Agent OS is a deployed product with UI + identity, not a lib you assemble |
| [MetaGPT](https://github.com/geekan/MetaGPT) | Python framework | Python | "Software company" role agents | No | MetaGPT ends at code-gen scripts; Agent OS adds tenancy, audit, bench, UI shell |
| [CrewAI](https://github.com/crewAIInc/crewAI) | Python framework | Python | Role/crew orchestration | Partial (CrewAI Enterprise SaaS) | .NET-native, on-prem-first, SDLC-specialized vs generic crews |
| [Devin](https://www.cognition.ai/) (Cognition) | Hosted SaaS | Closed | Autonomous SWE agent | Cloud only, closed | Agent OS is open, self-hostable, inspectable; Devin is a black box |
| [Cursor](https://cursor.com/) | IDE (VS Code fork) | Closed | In-editor coding assist | No (per-dev) | Cursor = single-dev IDE; Agent OS = team/tenant SDLC platform |

**The gap Agent OS fills**: every popular framework is either (a) a Python library requiring you to be the integrator, or (b) a closed SaaS. Nobody offers a **.NET-native, desktop-shell, multi-tenant, on-prem-ready SDLC agent platform with a built-in quality benchmark**. That is a defensible niche for enterprises that (1) live in the Microsoft/.NET stack, (2) can't ship code to a third-party cloud (finance, gov, healthcare), and (3) want to *measure* agent output, not trust it blindly.

### Target personas

1. **Enterprise dev team** inside a .NET shop — wants agent assistance but cannot use Cursor/Devin because code can't leave the building. On-prem + Keycloak SSO is the unlock.
2. **Software consultancy** — runs many client projects, needs hard tenant isolation so client A never sees client B's code or prompts. Multi-tenant is the unlock.
3. **Internal platform / DevEx team** — wants to offer "agents as a service" to their own engineers with audit, cost control, and RBAC. Open-core + observability is the unlock.

---

## 2 — Phase Roadmap (12–18 months)

Each phase: goal · duration · deliverable · KPI.

### Phase 0 — Thesis prototype (NOW)
- **Goal**: pass the master's defense at HUBT.
- **Duration**: until defense date.
- **Deliverable**: KC1–KC5 benchmark results, 5 working agents, Blazor UI shell, Keycloak multi-tenant OIDC (Epic D), MAF SDK integration (in progress).
- **KPI**: defense passed; KC1–KC5 produce reproducible quality numbers; demo runs single-F5 via Aspire.

### Phase 1 — Open-source launch (months 0–3 post-defense)
- **Goal**: turn a private thesis repo into a credible OSS project.
- **Deliverable**: clean README with architecture diagram + GIF demo; `docs/` (getting-started, architecture, agent authoring); `CONTRIBUTING.md` + issue/PR templates (already present); 3–5 runnable examples; 5 blog posts (why .NET agents, multi-tenant agent isolation, the KC benchmark, LLM gateway design, on-prem story); a landing page.
- **KPI**: 500 GitHub stars; 10 external issues filed; first non-author PR merged.

### Phase 2 — Plugin / extension architecture (months 3–6)
- **Goal**: stop being a fixed 5-agent app; let users extend.
- **Deliverable**: a plugin contract (agent plugins, prompt-template packs, BYO LLM provider adapters); a `agent-scaffold`-style codegen for third parties; a minimal registry/discovery; 2–3 first-party sample plugins.
- **KPI**: 5 external contributors; 10 community plugins published.

### Phase 3 — Enterprise features (months 6–9)
- **Goal**: make it buyable by an enterprise.
- **Deliverable**: SSO beyond Keycloak (Okta, Microsoft Entra ID); fine-grained RBAC (per-project, per-agent, per-action); full audit log + export; SOC 2 readiness checklist + evidence collection; an on-prem deployment guide (Docker Compose + Helm/k8s + azd).
- **KPI**: 1 signed enterprise pilot (even unpaid/design-partner).

### Phase 4 — Observability + AIOps (months 9–12)
- **Goal**: make agent ops cheap and self-improving.
- **Deliverable**: deep OpenTelemetry with GenAI semantic conventions; a cost-optimization dashboard (spend by agent/model/tenant/day — the `cost-report` skill is the seed); automatic prompt tuning (the `prompt-tune` skill productized); prompt-drift detection with regression alerts.
- **KPI**: demonstrate ≥90% cost reduction vs naive "always call the biggest model" baseline on the KC benchmark workloads (via routing + caching + cheaper-model fallback).

### Phase 5 — Marketplace + ecosystem (months 12–18)
- **Goal**: a self-sustaining ecosystem.
- **Deliverable**: an agent/prompt-pack marketplace with revenue share; certified prompt packs (curated, benchmarked); community contribution flow with quality gating via the KC bench.
- **KPI**: 20+ marketplace items; first revenue (sponsor, paid pack, or SaaS seat); 50 cumulative external contributors.

---

## 3 — Technical Pillars

Long-term architecture commitments. Each: where we are → where we go.

### 3.1 Multi-agent orchestration framework
- **Now**: 5 fixed agents (Requirement/Coding/Testing/QA/Orchestrator) in `AgentOs.Modules.Pipeline`, registered via `PipelineModule.AddAgents`.
- **Future**: user-defined agents via the plugin contract; orchestration graph (DAG, not just linear pipeline); conditional routing and parallel fan-out; no vendor lock in the orchestration layer (our own contracts, MAF/SK as *one* possible engine, not the only one).

### 3.2 LLM provider abstraction
- **Now**: Claude + Azure OpenAI through `IChatClient` / `ILlmClientFactory` (already routed via official SDKs — hand-rolled clients retired).
- **Future**: add Microsoft Agent Framework (MAF) / Semantic Kernel / OpenAI Agent Builder as pluggable engines; **local models** (Ollama, vLLM) for fully air-gapped on-prem; capability negotiation (tool-calling, vision, context window) so the router picks per task. Rule: **always ≥2 providers wired** to avoid lock-in.

### 3.3 State management
- **Now**: in-memory + Postgres (per-module DbContext, modular monolith).
- **Future**: event sourcing for agent runs → replay + time-travel debugging of a pipeline execution; snapshot a run, branch it, re-run with a different prompt/model. This doubles as the regression harness for prompt drift.

### 3.4 Observability
- **Now**: structured logs (good enough to drive `cost-report`).
- **Future**: OpenTelemetry GenAI semantic conventions (spans per LLM call with model, tokens, cost as attributes); exemplar traces linking a metric spike to the exact trace; cost telemetry as a first-class signal, not a log-scrape.

### 3.5 Security
- **Now**: Keycloak OIDC, JWT bearer, tenant claim, row-level isolation (Epic D).
- **Future**: zero-trust between modules; prompt-injection defense (strict template boundaries, input/output classification); output filtering (PII, secret leakage); **sandboxed code execution** (the Coding/Testing agents must run code in a jailed container, never the host) — this is the single biggest security gap once agents execute what they write.

### 3.6 Multi-tenant
- **Now**: row-level isolation via tenant context middleware (Epic D in progress).
- **Future**: tiered isolation — row-level (cheapest) → schema-per-tenant → fully isolated infra per tenant (for the paranoid enterprise). Sell isolation level as a pricing axis.

### 3.7 Knowledge management
- **Now**: none (agents are stateless re: codebase knowledge beyond prompt context).
- **Future**: RAG over the tenant's own codebase + docs; a vector DB (pgvector first — stays in the existing Postgres); a document-ingestion pipeline; citation tracking so an agent's claim links back to the source doc/line (directly reuses the citation discipline already in this repo's tooling).

### 3.8 Tool execution
- **Now**: agents mostly produce text/code.
- **Future**: MCP integration (consume the growing MCP tool ecosystem); native API calls; sandbox shell; browser automation for agents that need to verify a running app. MCP-first because it's becoming the de-facto tool protocol.

### 3.9 UI
- **Now**: Blazor desktop shell, KDE Breeze theme, interactive circuits.
- **Future**: Blazor SSR + interactive islands for faster first paint; mobile-responsive read-only view (check a run from your phone); a **CLI** for CI/headless use — the same backend, no browser. The CLI is what makes Agent OS usable inside pipelines, not just at a desk.

---

## 4 — Business Model Options

| Model | What's free | What's paid | Pros | Cons |
|-------|-------------|-------------|------|------|
| **OSS-first** (MIT/Apache + sponsor) | Everything | Sponsorship goodwill | Max adoption, simple | Revenue weak & unpredictable (Hangfire-Pro shows sponsor-only rarely funds a solo dev) |
| **Open core** | Core agents, single-tenant, basic auth | SSO, fine-grained RBAC, audit/compliance, support, schema/infra isolation | Adoption *and* a clear paid line; enterprise features are exactly what can't be free anyway | Must police the core/enterprise boundary carefully |
| **SaaS** | Trial | Hosted seats | Recurring revenue, you control infra | On-prem buyers (the whole point) can't use it; ops burden for a solo dev |
| **Consulting** | The OSS | Your time | Immediate cash, deep user insight | Doesn't scale; trades hours for money |

### Recommendation: **Open core**

Reasoning: the target personas (on-prem .NET enterprises, consultancies) *cannot* use SaaS — their entire reason to pick Agent OS is keeping code in-house. That kills pure SaaS as the primary model. Pure OSS-sponsor under-funds a solo dev. Open core fits the product's DNA: the features enterprises will pay for (SSO/Entra, fine-grained RBAC, audit/SOC 2, infra-level tenant isolation, priority support) are precisely the ones that *should* be gated, and they're already on the Phase 3 roadmap. Start OSS to build adoption (Phase 1), introduce the enterprise edition at Phase 3 when those features exist. Consulting fills the cash gap in between and feeds requirements. SaaS stays a *later optional* offering (Agent OS Cloud) for teams that don't want to self-host, not the core bet.

---

## 5 — Risk & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| **LLM cost explosion** | A runaway loop burns the month's budget in hours | Cost guard with hard per-tenant/per-run budget caps; budget alerts; cheaper-model routing (Phase 4); cache identical prompts |
| **Prompt drift over time** | Yesterday's good prompt silently regresses | Version prompts v1→vN; snapshot fixtures; regression test on the KC bench in CI (`prompt-tune` already scores variants) |
| **Vendor lock-in** | Provider changes pricing/API, you're stuck | Provider abstraction (`IChatClient`); contractually keep ≥2 providers wired at all times; add a local model (Ollama/vLLM) as the always-available floor |
| **Agent loop / hallucination** | Agents confidently produce wrong code, loop forever | QA agent as a gate; human-in-the-loop checkpoints; **max-iteration cap** per run; the KC bench catches quality regressions before release |
| **Prompt injection** | Malicious input hijacks an agent | Strict template boundaries (untrusted input never concatenated into the system prompt); input/output classification; output filter for secrets/PII |
| **Multi-tenant data leak** | Tenant A sees tenant B's code → trust dead, possibly legal | Row-level isolation tests as a CI gate (`TenantIsolationTests`); audit the tenant-context middleware on every PR touching data access; tiered isolation for high-assurance tenants |
| **Sandbox escape** | Agent-run code touches the host | Never execute generated code on the host; jailed container with no network/secret access (Phase 3/5 dependency before any "agent runs your tests" feature ships) |
| **Solo-dev bandwidth** | One person can't build all this | Invest in docs *early* (Phase 1) to recruit contributors; keep the modular monolith so contributors own one module; ruthless prioritization (open core > breadth) |

---

## 6 — Quarter 1 Sprints (post-defense, 12 weeks)

One-week sprints, file/feature level. Assumes Epic D (Keycloak/OIDC) landed.

### Sprint 1 — Docs foundation
- Rewrite `README.md`: architecture diagram, demo GIF, single-F5 quickstart.
- `docs/getting-started.md`, `docs/architecture.md`.
- **DoD**: a stranger can clone → run → see the UI in <15 min following docs only.

### Sprint 2 — Landing + first blog
- Static landing page (GitHub Pages) — pitch, screenshots, "star us".
- Blog post #1: "Why we built AI SDLC agents in .NET, not Python".
- **DoD**: landing live; blog published; both link to repo.

### Sprint 3 — Plugin contract (design + core)
- Define `IAgentPlugin` / prompt-pack / provider-adapter contracts in a new `AgentOs.Plugins.Abstractions`.
- Loader + discovery (scan a plugins folder).
- **DoD**: app boots with zero plugins and with a stub plugin, no code change.

### Sprint 4 — First sample plugin + scaffold
- Extend `agent-scaffold` skill to emit a *plugin* (not just an in-tree agent).
- Ship one real sample plugin (e.g. a "Docs agent").
- **DoD**: `dotnet run` discovers and runs the sample plugin; docs show how to author one.

### Sprint 5 — BYO LLM: local models
- Ollama adapter via `IChatClient`.
- Config-driven provider selection per agent.
- **DoD**: run a full pipeline against a local Ollama model, no cloud calls.

### Sprint 6 — BYO LLM: vLLM + OpenAI-direct
- vLLM (OpenAI-compatible) adapter; OpenAI-direct adapter.
- Capability metadata per provider (context size, tool-calling).
- **DoD**: 4 providers selectable (Claude, Azure, Ollama, vLLM/OpenAI); KC bench runs across all.

### Sprint 7 — Observability: OTel spans
- Wrap every LLM call in an OTel span (model, tokens, cost attributes, GenAI conventions).
- Export to a local collector (Aspire dashboard).
- **DoD**: a pipeline run shows a full trace with per-call cost in the dashboard.

### Sprint 8 — Cost dashboard
- Productize `cost-report` into an in-app "Cost" tab (by agent/model/tenant/day).
- Budget caps + alert when a tenant nears its cap.
- **DoD**: dashboard renders real spend; exceeding a test cap fires an alert.

### Sprint 9 — Enterprise SSO: Entra ID
- Add Microsoft Entra ID as an OIDC provider alongside Keycloak.
- Map Entra groups → Agent OS roles.
- **DoD**: log in via Entra; role mapping correct; tenant resolved.

### Sprint 10 — Enterprise SSO: Okta + RBAC polish
- Okta OIDC provider.
- Fine-grained policy: per-project / per-agent authorization.
- **DoD**: Okta login works; a member without project access is denied at the API.

### Sprint 11 — Launch prep
- Audit log export; security-hardening checklist done; final demo recording.
- Polish onboarding; seed `good-first-issue`s.
- **DoD**: launch-ready repo; 10 starter issues filed.

### Sprint 12 — Public launch
- Post to Hacker News (Show HN), r/dotnet + r/LocalLLaMA, Product Hunt.
- Blog posts #2–#5 staggered.
- Triage incoming issues same-day.
- **DoD**: launched on all 3 channels; respond to every issue/comment in 24h.

---

## 7 — Connection with the Thesis

This roadmap *is* the answer to "what happens after you submit?" — it maps onto thesis Chapter 3 (đề xuất triển khai + chính sách + công cụ + đào tạo + hạn chế):

| Thesis Ch3 section | Roadmap element |
|--------------------|-----------------|
| **Triển khai** (deployment proposal) | Phase 1 OSS launch + Phase 3 on-prem deploy guide (Docker/Helm/azd) |
| **Chính sách** (policies) | Phase 3 RBAC + audit + SOC 2; Section 5 security risks/mitigations |
| **Công cụ** (tooling) | Phase 2 plugin architecture; Phase 4 observability + cost + prompt-tune tooling |
| **Đào tạo** (training/enablement) | Phase 1 docs + examples + blog; Sprint 1–2 getting-started |
| **Hạn chế** (limitations) | Section 5 risk table — names the real limits (cost, drift, lock-in, sandbox, solo bandwidth) honestly |

- **Ch2.2** (multi-tenant) ← Technical Pillar 3.6 (tiered isolation).
- **Ch3.2** (security policies) ← Technical Pillar 3.5 + Section 5 security rows.

If the defense committee asks "kế hoạch sau khi nộp luận văn?", this document is the answer: a concrete, costed, phased plan — not aspiration.

---

## 8 — Definition of Success (3 horizons)

### 3 months
- **Defense passed.**
- Open-source launched: **500★** on GitHub, first external PR merged, 10 issues from non-authors.

### 12 months
- **1 enterprise pilot** running (design partner or paid).
- **50 cumulative external contributors.**
- Ecosystem: **20 plugins** published.
- Cost story proven: ≥90% reduction vs naive baseline on KC bench.

### 18 months (pick the lane that fits where adoption goes)
- **Open-core / community lane**: **5,000★**, **5 companies in production**, sustainable contributor base.
- **OR commercial lane** (if SaaS/enterprise edition shipped): **$50K MRR** from enterprise seats + hosted Agent OS Cloud.

Both lanes are valid; the data from months 3–12 (do enterprises pay, or does the community grow faster?) decides which to lean into. Don't pre-commit — instrument adoption and let the numbers choose.

---

*Next: see [Epic-D-Overnight-Plan.md](Epic-D-Overnight-Plan.md) for the immediate Keycloak/OIDC work feeding Phase 0 → Phase 1.*
