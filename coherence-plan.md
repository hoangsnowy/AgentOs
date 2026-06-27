# AgentOS — Coherence Program v2: one seamless engine, fast + enterprise-grade, docs, community

> **SUPERSEDED as the planning document by [ROADMAP.md](ROADMAP.md) (2026-06).**
> Phases 0–2 shipped (#52–#73): A1 + safety baseline (S1–S3) + A2a/A2b/A3 cross-links, plus E4
> health probes, E7 store fix, F1 CI hardening. The remaining workstreams (B, E, F2, G, C, D) are
> absorbed into ROADMAP.md §3 at milestone granularity. This file remains the **engineering
> appendix** — its PR-level slicing is the historical implementation reference.
>
> **Renames since this was written:** the "Spine" desktop app shipped and was later **renamed "Board"**
> — `SpineApp.razor` is now `Components/Pages/BoardApp.razor` (and the `.spine-*` CSS / `"spine"`
> catalog key are now `.board-*` / `"board"`). Treat every `SpineApp.razor:NNN` anchor below as
> `BoardApp.razor` (line numbers long since drifted — Phases 0–2 are shipped, so these are no longer a
> live edit target). The architectural **"spine"** (the requirement→remote-repo-execution backbone)
> is a separate concept and keeps that name.

> **Status of this doc.** Revised after a grounding pass that (a) verified every file:line anchor against current code, (b) audited performance + enterprise hardening with evidence, (c) red-teamed the sequencing. The original plan predated PR #54 and is stale in places — corrections are marked **⚠ CHANGED**. Goal added this session: the product must be **fast** and meet **enterprise standards**, not only coherent.

---

## 0. What is already shipped (do NOT re-plan)

| Done | Commit / PR | Effect |
|---|---|---|
| `GitHubClientFactory` — one Octokit client for both PR paths | #53 | de-duped PR plumbing |
| Pipeline↔Workflow cross-link | #53 | apps reference each other |
| `GraphExecutor` — Workflow runs the real 5 agents | #52 | Workflow no longer decorative |
| **⚠ A1 — unify repo+token credentials (Pipeline "Open PR" targets a workspace)** | **#54 (`82f2ad6`)** | **A1 is MERGED, not todo** |

**⚠ CHANGED — A1 is done.** Grounding confirms the workspace overload `OpenPrAsync(PipelineResult, WorkspaceDescriptor, title, body, ct)` already exists at `src/AgentOs.Modules.Integration/IGitHubPrService.cs:26`; `OpenPrCoreAsync` already extracts steps 1–4 at `GitHubPrService.cs:80-152`; `Pipeline.csproj:13` already references AppConfig. The remaining A1 residue is small and folds into A3 (below): a **third** inline `WorkspaceDescriptor` build the original plan missed at `SpineApp.razor:1116` (EnsureLabels seed) is not yet extracted into the shared helper. Confirm A1's *full-stack, user-driven* verification actually happened (board-PAT PR landing) — if not, that verification is an open item, not new code.

---

## 1. Corrected anchors (the original plan's line numbers drifted)

Use these when implementing — verified against current `HEAD`:

| Plan said | Reality |
|---|---|
| `IGitHubPrService.cs:18` add overload | **already exists** at `:26` |
| `GitHubPrService.cs:34-125` steps 1–4 | refactored into `OpenPrCoreAsync` at `:80-152`; `:34-58` is now the Settings-token overload |
| `WorkspaceDescriptor.cs:64` | valid — full path `src/AgentOs.Domain/Workspaces/WorkspaceDescriptor.cs:64` |
| `SpineApp.razor:1582`/`:1238` inline descriptor | `:1582` + `:1237` (off by one) **and a third at `:1116`** |
| `PipelineStudio.razor:250-264` Push card | `:252-266` |
| `SpineApp.razor:719` tenant-claim read | **`:826`** (`:719` is the `[CascadingParameter]` decl) |
| `RuntimeOverrides.cs:51-55` GitHub fields | valid |
| `RemoteSessionEntity.cs` add Brain | valid — no Brain yet; mirror `RunOnMachine` at `:49` |
| migration `20260604143532_AddRunOnMachine` | valid template at `src/AgentOs.Modules.Sessions/Persistence/Migrations/` |
| toggles `:456`/`:691`; `:1389 :1536 :1544 :1619 :912 :358` | all valid |
| `PipelineStudio.razor:335` OpenInWorkflow | **method at `:363`, `WM.OpenApp` at `:368`** |
| `WindowManagerService.cs:46-56` launch-tab pair | valid (add the new payload pair next to it) |
| `AppCatalog.cs:39-41` captions | valid |
| Integration sees Workspace types w/o new ref | **confirmed** — `Integration.csproj:9 → Domain` |
| Pipeline may ref AppConfig | **already referenced** — `Pipeline.csproj:13` |

---

## 2. Re-sequenced roadmap (the big change)

The original order was **A→B→C→D** (coherence → audit → docs → community), with performance/hardening absent. The red-team found that A2 (Spine runs the 5-agent pipeline) **widens two unguarded surfaces** — server-side token spend and remote shell execution — so shipping it before any hardening is unsafe. New order:

```
Phase 0  DONE      A1 (#54), GraphExecutor (#52), GitHubClientFactory + cross-link (#53)
Phase 1  SAFETY    S — spend gate on every server-token entrypoint + per-tenant rate/concurrency limit
                       + runner_shell default-deny/allowlist + NMax admin-bound   [BLOCKS A2]
Phase 2  COHERENCE A2a (engine unification + greenfield router + spend-gate exit criteria)
                   A2b (Quality/Quick toggle UX)
                   A3  (bidirectional cross-links + fold in A1 residue at :1116)
Phase 3  AUDIT     B  (FEATURE-MATRIX.md) — may fold into A3
Phase 4  PERF      E  (the 7 grounded perf findings; scale-out honesty)
Phase 5  HARDEN    F1 (CI/supply-chain) + F2 (secrets/KeyVault/DP keyring/orphaned-secret cleanup)
Phase 6  ENTERPRISE G  (audit export/retention, RBAC depth, DR, alerting, secret rotation, data deletion)
Phase 7  DOCS+COMM C1/C2 docs, D community pack — security/ops docs written WITH F/G so they describe the hardened state
```

**Why safety moves first (red-team HIGH):** `IssueWorkAgent` (the Spine "Quick" brain) spends server LLM tokens but **never calls `IBudgetGuard`** — the only budget gate is wired solely into `PersistingOrchestratorAgent.RunAsync` (`:43-44`). A2 routes the expensive 5-agent loop through the same unmetered Spine path. Shipping A2 first = an unmetered, unthrottled, server-token loop a single tenant (or a runaway QA loop at `NMax`) can use to burn unbounded spend, plus `runner_shell` arbitrary remote execution that is **default-permissive**. So a minimal safety baseline is an A2 prerequisite, not deferred F work.

---

## Phase 1 — Workstream S: Safety baseline (NEW — blocks A2)

**Goal:** every entrypoint that spends server tokens or runs remote commands passes a gate, before A2 widens those surfaces. Small, single-purpose, security-critical PRs — keep them reviewable.

- **S1 — Budget gate on all server-token entrypoints.** Wire `IBudgetGuard.EvaluateAsync` into the Spine/`IssueWorkAgent` entry (and the future unified run-entry), not just `PersistingOrchestratorAgent`. Add a **per-iteration** check so the QA loop can't blow the cap mid-run. **Honest limit (red-team HIGH):** the existing `IBudgetGuard` (`src/AgentOs.Modules.Pipeline/Cost/BudgetGuard.cs`) is a *monthly, post-hoc, month-to-date* cap that (a) is a no-op on the no-DB path (spend always 0), (b) has a read-lag race — N concurrent runs read the same pre-run total and all pass. So S1 must also add an **atomic reservation/decrement**, not rely on the stale monthly read. Do **not** let the plan claim "cost guardrail done" on the monthly cap alone.
- **S2 — Per-tenant rate + concurrency limit at the LLM seam.** `AddRateLimiter` (per-tenant token-bucket + fixed-window on run endpoints) + a max-concurrent-runs-per-tenant gate around the orchestrator and around `IssueWorkAgent.MaxParallelRepos` (default 3). This is the in-flight throttle the monthly budget guard is **not**. Evidence it's missing: `grep AddRateLimiter|UseRateLimiter` → no matches.
- **S3 — `runner_shell` hardening.** `runner_shell` (`src/AgentOs.Modules.RemoteAgent/RunnerShellTool.cs`) executes arbitrary shell on the paired machine; the only gate is `IToolPolicy`, which is **default-permissive** until a per-tenant `tools/enforce` flag flips (`AppConfigToolPolicy.cs`; `ToolPolicyTests` confirm `runner_shell` allowed by default). Ship: an enforced baseline allowlist (or default-deny) + per-tenant concurrency cap as the **default for any multi-tenant/Aspire deploy**. Default-permissive remote shell is not enterprise-acceptable.
- **S4 — `NMax` becomes admin/tenant-bounded, not a free user dial.** `NMax` (QA convergence cap) is today the de-facto cost ceiling. The A2b Quality/Quick toggle raises it. Make `NMax` an admin/tenant-policy max; the user toggle selects **within** that bound, never above it. (red-team MED — privilege inversion.)
- **Tests:** budget gate hit on the Spine path (a bug-fix/over-cap run is refused); rate-limiter rejects an over-rate burst; `runner_shell` denied by default without an allowlist entry; `NMax` toggle clamped to the admin bound.
- **Verify (honest surface):** **full Aspire stack only** — S1/S2 are no-ops on the no-DB standalone path (spend=0, tenant scoping absent). Exercise with a **real cap configured and a real over-cap run**, and a real over-rate burst. Hand the user a drivable URL + creds. Unit-green is necessary, not sufficient.

---

## Phase 2 — Coherence finish

### PR A2a — Spine runs the 5-agent pipeline (engine unification + greenfield router + spend-gate)
**Goal:** a Spine ticket can run the 5-agent **Quality** loop against a workspace repo → commit generated code+tests → PR, as an alternative to `IIssueWorkAgent`. Honest limit: the pipeline is **greenfield** (new files) → scope to new-feature/scaffolding tickets; repo-grounded editing deferred (but see the deferral trap below). `IssueWorkAgent` stays the "Quick" brain.

- `RemoteSessionEntity.cs` — add `string Brain = "Quick"` ("Quick"=IssueWorkAgent, "Quality"=pipeline). Additive EF migration `AddSessionBrain` mirroring `RunOnMachine` (`:49`) / `20260604143532_AddRunOnMachine`.
- New `src/AgentOs.Modules.Pipeline/Sessions/IssueToStoryAdapter.cs` — pure `ToUserStory(IssueWorkRequest, nMax)`.
- `SpineApp.razor RunSession` `Task.Run` (`:1536`) — branch on `Brain`. "Quality" resolves `IPipelineClient`+`IGitHubPrService` **lazily inside the scope** (eager-DI landmine), under the existing `AmbientIdentity.Push` (`:1544`), streams, on terminal Result hydrates the **primary** repo's `WorkspaceDescriptor` (the A1 helper) → `OpenPrAsync(result, descriptor, title, body)` → update run result / recompute status; mirror the failure catch at `:1619`. Progress: each `PipelineStreamEvent.Progress` → `ISessionRunFeed.Publish` (Spine subscribes at `:912`); keep the map a pure static method.
- **⚠ Greenfield bound is a HARD router, not a sentence (red-team HIGH).** Classify the ticket (new-feature/scaffold vs bug-fix/edit) and **hard-route**: greenfield → pipeline, edit → IssueWork/repo-grounded path, ambiguous → **refuse with a clear message**, never silent best-effort. `IssueWorkAgent`'s prompt is an "explore→implement→build→test→commit" *edit* loop, so a bug-fix ticket flowing into the greenfield pipeline produces duplicate/garbage files. **Ship the classifier + a test asserting a bug-fix-labelled ticket does NOT reach the greenfield pipeline.**
- **Exit criteria (from Phase 1):** every server-token entrypoint A2a touches passes the S1 budget gate + S2 concurrency cap. A2a does not merge until S1–S4 are in.
- **Tests:** `IssueToStoryAdapterTests`; `SessionBrainTests` (round-trip + default + migration applies); progress-map (pure); **ticket-type router** (bug-fix refused).
- **Verify:** standalone = tests + migration apply. Full-stack (user-driven) = new-feature ticket, Brain=Quality → Spine feed shows pipeline Step events → PR with generated code+tests on the workspace repo via board PAT; bug-fix ticket is **refused**, not garbage-scaffolded; Brain=Quick still runs IssueWorkAgent.

### PR A2b — Quality/Quick toggle UX
**Goal:** the user-facing brain switch, with honest cost semantics.
- Brain `<select>` beside the run-on-machine toggles (`:456`,`:691`), bound to `_brain`, threaded through `PersistSessionAsync` (`:1389`). Reuse `.spine-ai-toggle`.
- **Guards:** Quality ⊕ RunOnMachine mutually exclusive (pipeline is server-side). **Label the cost (red-team MED):** "Quality — up to N iterations, higher server-token cost"; default **Quick**; surface the iteration/estimate. Toggle state lives in a **scoped** service (circuit constraint), never singleton. `NMax` selection clamped to the S4 admin bound.
- **Tests:** toggle state scoped; clamp to admin `NMax`; Quality+RunOnMachine rejected.
- **Verify:** full-stack — pick Quality, confirm the cost label + iteration cap shown, confirm a second user's toggle doesn't bleed.

### PR A3 — Bidirectional cross-links + fold A1 residue
**Goal:** close the triangle so all three surfaces read as one engine. Tiny UI, reuse `WindowManagerService.OpenApp` + `AppCatalog`, **no new CSS** (run `check-classes.ps1` before merge).
- `SpineApp.razor` ticket actions (`:358`) — "Open in Pipeline" via `WM.OpenApp("pipeline",...)` (mirror `OpenInWorkflow` — **method `:363`, call `:368`**); prefill via a one-shot `RequestLaunchPayload/ConsumeLaunchPayload` added next to the launch-tab pair (`WindowManagerService.cs:46-56`); `PipelineStudio` reads it in `OnInitialized` into `_description`.
- **A1 residue:** extract the third inline `WorkspaceDescriptor` build at `SpineApp.razor:1116` into the shared A1 helper (the original plan only listed `:1582`/`:1237`).
- `AppCatalog.cs:39-41` captions so the three read as one engine (Pipeline="run on a sandbox story", Workflow="edit as a visual graph", Spine="run on a real ticket → PR").
- **Tests:** WM payload one-shot (set→consume→null); `AppCatalog` resolves pipeline/workflow/spine keys.
- **Verify:** standalone tests + phantom-CSS clean; full-stack = Spine ticket → Open in Pipeline prefilled; round-trip Pipeline↔Workflow↔Spine.

---

## Phase 3 — Workstream B: Feature-matrix audit
New `docs/FEATURE-MATRIX.md` — every module + 11 desktop apps + agents/gateway/governance/cost/plugins/spine, each tagged **Working / Degraded-standalone / Stub / Gap** with file-path citations. Fix only cheap obvious dead-ends (stale Codex-branded `AGENTS.md` vs `CLAUDE.md`; orphaned routes). **Consider folding into A3** (red-team MED: B is doc-ish, not code). Larger gaps → roadmap items.

---

## Phase 4 — Workstream E: Performance (grounded)

> Already good, do NOT re-add: OpenTelemetry fully wired (`ServiceDefaults/Extensions.cs:42-62`), HTTP resilience `AddStandardResilienceHandler` (`:28-32`), **AsNoTracking on all read repos** + composite `(TenantId,…)` indexes in every `OnModelCreating`, AppConfig 15s cache (`EfAppConfigStore.cs:25-74`), LLM pool + round-robin + 429 failover + per-attempt OTel/cost (`PooledChatLlmClient.cs:103-160`), response compression (Brotli+Gzip) on both hosts, scoped per-circuit UI state.

| ID | Issue | Evidence | Fix | Eff | Tag |
|---|---|---|---|---|---|
| **E1** | Migrations applied at startup in 6 modules, **sequentially**, before the host listens; no advisory lock → slow cold start + multi-instance boot race | `MigrateAsync` in `PipelineModule.cs:101`, `AppConfigModule.cs:53`, `ToolsModule.cs:84`, `TenantsModule.cs:92`, `WorkspacesModule.cs:68`, `SessionsModule.cs:88`; loop `ModuleLoader.cs:60-63`; called at `Api/Program.cs:88` + `Web/Program.cs:189` before `app.Run()` | Move migration to a deploy-time `dotnet ef database update` / one-shot Job; gate runtime `MigrateAsync` behind an env flag (off in prod). If kept in-proc, wrap in `pg_advisory_lock` so one replica migrates and readiness stays unready until done | M | startup |
| **E2** | Blazor Server single-instance: `AddInteractiveServerComponents` with **no** Azure SignalR backplane / sticky sessions → circuits drop on reconnect to another replica | `Web/Program.cs:40-41`, `:339-340`; `grep AddAzureSignalR\|AddStackExchangeRedis` → none | For >1 replica: Azure SignalR Service (`AddSignalR().AddAzureSignalR()`) **or** Container Apps `stickySessions` + a Redis/Azure backplane for the RemoteAgent hub. Until then document single-instance | L | scaleout |
| **E3** | DataProtection added **bare** on both hosts — ephemeral per-replica keyring → OIDC correlation/nonce + auth + antiforgery cookies minted on one instance fail to decrypt on another (login loops; all sessions die on restart/scale) | `Web/Program.cs:43`, `Api/Program.cs:38` `AddDataProtection()`; `grep PersistKeysTo\|SetApplicationName` → none; keys used at `Web/Program.cs:134-137`,`:106-118` | `AddDataProtection().PersistKeysToAzureBlobStorage(...).ProtectKeysWithAzureKeyVault(...).SetApplicationName("agentos")`; mounted volume for Aspire local. **Required before any multi-instance / zero-downtime restart** (= hardening H5) | M | scaleout |
| **E4** | `/health` + `/alive` gated behind `IsDevelopment()` → **not mapped in prod**, breaking Container Apps liveness/readiness. Also `MapDefaultEndpoints` is never called; both hosts hand-roll an unconditional `/health` but no readiness-gated `/alive` | `ServiceDefaults/Extensions.cs:83-90`; hand-rolled `Api/Program.cs:116`, `Web/Program.cs:210` | Map `/health` (readiness, all checks) + `/alive` (liveness) **unconditionally**; add an EF/Postgres readiness check that reports unready until migration completes; point probes at them | **S** | scaleout |
| **E5** | LLM gateway has pooling + failover but **no response cache and no streaming** — the dominant latency+cost path (1 + 3×NMax sequential calls) re-pays full cost every run | `PooledChatLlmClient.cs:122` blocking `GetResponseAsync`; `grep GetStreamingResponseAsync` → none; `grep HybridCache\|IMemoryCache\|OutputCache` → none; sequential loop `PipelineOrchestrator.cs:105-165` | Prompt-keyed `HybridCache` (provider+model+system+user+temp) for temperature-0 calls; token-level streaming via `GetStreamingResponseAsync` for first-token latency; run independent agents concurrently where the QA loop permits | L | latency |
| **E6** | No rate limiting (= S2 / hardening H8) | `grep AddRateLimiter\|UseRateLimiter` → none; only the coarse monthly `BudgetGuard` | Delivered by **S2** — leave here only as the perf cross-ref | M | cost |
| **E7** | Sync-over-async in the **singleton** `OrchestrationStore`: `.GetAwaiter().GetResult()` blocks a pool thread, runs at host build (eager `LoadOrSeed`); in-mem `_graphs` cache never invalidated cross-instance → stale graphs on other replicas until restart | `OrchestrationStore.cs:155`,`:163`,`:34`; `AddSingleton` `Web/Program.cs:173`; cache `:26` | Make the store async end-to-end + resolve repo per-call (drop the singleton dict) **or** back the cache with Redis + pub/sub invalidation; drop eager `LoadOrSeed` (lazy on first use) | M | scaleout |

**Sequencing note (red-team MED):** E5/E7 touch the same hot-path files A2 touches (`PooledChatLlmClient`, orchestrator). Decide per-file whether the redesign belongs in A2a or E — **don't split one file's redesign across two milestones.**

**Verification honesty (red-team HIGH):** E1/E2/E3/E7 are **scale-out** claims — *unprovable on single-instance Aspire*. Single-instance will look green while hiding backplane/sticky-session/keyring requirements. Each scale-out slice is either verified on a **≥2-replica run** or the claim is downgraded to **"single-instance verified, scale-out design only."** E4 is the only cheap one (S).

---

## Phase 5 — Workstream F: Hardening / supply chain

> CodeQL is **done** (`.github/workflows/codeql.yml`, this session). Already in place: governance `IToolInvocationLog`, DataProtection encryption, per-tenant row isolation, OTel.

### PR F1 — CI / supply chain
| ID | Issue | Fix | Eff |
|---|---|---|---|
| H1 | No `.github/dependabot.yml` | add nuget + github-actions ecosystems | S |
| H2 | Actions pinned by floating `@v4` tag, not commit SHA (ci/codeql/azd/e2e) | pin SHAs | M |
| H3 | No `dependency-review` action on PRs | add the action | S |
| H4 | `ci.yml` + `e2e-realauth.yml` set no explicit `permissions:` (only codeql/azd do) | add least-priv `contents: read` | S |

### PR F2 — Secrets / runtime hardening
| ID | Issue | Evidence | Fix | Eff |
|---|---|---|---|---|
| **H7** | **Orphaned HS256 signing secret + operator password committed in `appsettings`; CD step hits a dead `/auth/token`** | `appsettings.json:49,50` | delete the dead secret block + the dead CD step; ensure operator/HS256 mode is fenced out of prod | M |
| H5 | DataProtection keyring ephemeral (= **E3**) | `Api/Program.cs:38`,`Web/Program.cs:43` no `PersistKeysTo` | `PersistKeysToDbContext`/Blob + `SetApplicationName` | M |
| H6 | No Azure Key Vault; prod secrets from env/appsettings | `grep AddAzureKeyVault` → none | `AddAzureKeyVault` for prod; reshape the secret-access contract (see deferral trap below) | M |

**⚠ Deferral trap (red-team MED):** `IRuntimeOverrides` (`src/AgentOs.Modules.Llm/IRuntimeOverrides.cs`) is the single tenant-scoped holder of `AnthropicApiKey`/`AzureApiKey`/`GitHubPat`, exposed as **sync properties** bridging the async AppConfig store. F2's secret work needs to change how these are read (secret provider / rotation / never materialize the PAT into a sync property). **Decide the secret-access contract (sync bridge vs async provider) as F2's first slice** — don't inherit the bridge as silent debt.

---

## Phase 6 — Workstream G: Enterprise-readiness (NEW — itemize + triage)

The red-team flagged table-stakes enterprise requirements absent from the whole plan. Even if some defer, **name each and record the decision** — silence reads as "forgotten" and fails an enterprise review.

| Concern | Why it gates enterprise | Decision |
|---|---|---|
| **RBAC depth** (admin/developer/viewer) | Today auth is operator vs Keycloak tenant claim — **any signed-in tenant user can disable the very guardrails S/F add** (flip tool policy, set budgets, run spend). RBAC gates F's tool-policy + budget admin actions | **In scope — blocks F's admin actions** |
| Audit export / tamper-evident trail | `IToolInvocationLog` + Tenants audit exist but no export/retention/integrity | scope: export + retention |
| Data retention / deletion (GDPR-style) | run artifacts + prompts may contain customer code; PAT/secrets | scope or explicit defer |
| Backup / restore + DR runbook | per-module Postgres schemas | runbook doc (ties to E1 migration job) |
| SLO / alerting | budget WARN only logs; no alert on over-cap / runner-disconnect / failed run | alert rules on OTel metrics |
| Secret rotation | tenant PAT + LLM keys never rotate | ties to H6/IRuntimeOverrides reshape |

---

## Phase 7 — Docs (C) + Community (D)

**⚠ Moved AFTER hardening (red-team LOW).** Architecture/feature docs may ship early, but the **operations/security/limits** sections must be written **with F/G** so they document the *hardened* state, not default-permissive tools / no rate limit / no spend gate as "how it works."

### C1 — Architecture + authoring guides
```
docs/architecture.md          big-picture + mermaid: modules, LLM gateway, pipeline flow, multi-tenant, the ONE engine / 3 surfaces (post Phase 2)
docs/CONVENTIONS.md           extracted from CLAUDE.md (style, tests, commits, E2E verification, branding, language)
docs/guides/authoring-a-module.md / -an-agent.md / -a-plugin.md / -a-tool.md / adding-an-llm-provider.md
docs/llm-gateway.md           PooledChatLlmClient, failover, cost, tool loop, + (post-E) caching/streaming
docs/multi-tenant.md          ITenantContext / AmbientIdentity / row-level isolation / Keycloak
```
Reuse (link, don't duplicate): `docs/SETUP.md`, `docs/DEPLOYMENT.md`, `docs/design/design-system.md`. README gets a docs index.

### C2 — ADRs + roadmap + governance
```
docs/adr/0001-modular-monolith.md, 0002-llm-gateway-abstraction.md, 0003-multi-tenant-isolation.md, 0004-one-engine-three-surfaces.md
docs/roadmap.md               PUBLIC roadmap distilled from AgentOS_Longterm_Roadmap.md (keep the private one private)
docs/governance.md            review cadence, contributor ladder, release policy, plugin-vetting
docs/operations.md            (written with F/G) limits, rate limits, budget, runner_shell policy, scale-out posture
```
Rewrite `CONTRIBUTING.md` → a 5–10-min "first PR" path linking the guides + design-review + verification rules.

### D — Community skills/rules pack
Mirror every skill into `.agents/skills/`.
- `.claude/skills/module-scaffold/SKILL.md` — `IModule` + DbContext + schema + migration + endpoint + DI + test stub.
- `.claude/skills/tool-scaffold/SKILL.md` — `ITool` + governance wiring + tests.
- `.claude/skills/lint-conventions/SKILL.md` — code-side `/design-review` analog: module dependency rule (Domain+SharedKernel only), nullable, naming, branding ("AgentOS" not "AgentOs"), "no vendor SDK in agents". Ships `check-conventions.ps1`.
- `.claude/skills/AUTHORING.md` + `TEMPLATE/` skeleton; update `.claude/skills/README.md` (discoverable by role).
- Optional tiny PR: CI step running `check-conventions.ps1`.

---

## Guardrails (every PR)

- Domain dependency-free; **Integration gains no new project ref** (Workspace types already visible via Domain); Pipeline→AppConfig already wired.
- Injected services have **side-effect-free ctors** — resolve LLM/pipeline clients **lazily inside run methods** (eager-DI crashed the circuit twice).
- In Blazor circuits read tenant from `AuthenticationState`/`AmbientIdentity`, **never** `ITenantContext`.
- CA analyzers are errors (CA1859 etc.); Release build **0 warnings**.
- Any UI change runs `pwsh .claude/skills/design-review/check-classes.ps1` (phantom-CSS).
- **No new island** — every feature dies into the unified engine + cross-links.
- One PR per milestone (multi-commit), branch → PR (fill template) → merge → delete branch. **Never push to main.** **Exception:** keep the security-critical PRs (S1–S4, F2) **small and single-purpose** even if total PR count exceeds the original 7.

```bash
dotnet build AgentOs.slnx -c Release            # 0 warnings
dotnet test  AgentOs.slnx -c Release
pwsh .claude/skills/design-review/check-classes.ps1
```

## Verification honesty (applies to S, E, F, G)

`dotnet run --project src/AgentOs.Web` (DevAutoLogin + no-op repos) proves only the **degraded** path. Anything touching **persistence / secrets / tenant / limits / scale-out** is **unverifiable** standalone — budget+rate limiter are no-ops without a DB (spend=0), tool allowlist needs AppConfig persistence, PAT handling needs the real encrypted store, tenant isolation needs real Keycloak, scale-out needs **≥2 replicas** (single-instance Aspire cannot prove it). For each such slice: pre-declare the honest surface in the PR test plan, exercise on the **full Aspire stack** (`infra/AgentOs.AppHost`, Web `:5180`, `operator`/`operator`) with a **real cap/limit configured and a real over-limit run**, and hand the user a drivable URL + creds.

## Explicitly deferred (with the traps named)
- `IRuntimeOverrides` GitHub fields + legacy PR overload: bridge now, delete later — **but** F2 must reshape the secret-access contract first (trap above).
- Repo-grounded editing by the 5-agent loop (Quality stays greenfield) — **state which engine owns "edit existing repo" after Phase 2**; the unified engine must still handle the most common enterprise task (fix a bug in an existing repo) or the "one engine" claim is a router over two half-capabilities.
- Multi-repo fan-out for Quality (primary repo only) — **reconcile against the already-shipped `IssueWorkAgent.MaxParallelRepos`** so E's concurrency work doesn't fork it.
- Merging IssueWorkAgent + 5-agent loop into one brain (stay two brains, one toggle); ADO PR creation for Quality; docs-site infra (markdown now).
```
