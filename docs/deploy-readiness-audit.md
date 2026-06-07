# AgentOS — Deploy-Readiness Audit (2026-06-07)

Multi-agent review: 10 dimensions → 80 raw findings → 42 adversarially confirmed, 1 refuted,
37 backlog. Build green (0 warn / 0 err), tests 470 pass / 5 skipped (live-LLM).

## Batch 3 (cloud hardening) — status

**Shipped + verified** (build 0/0, 470 tests pass, Aspire publish-manifest checked):
- ✅ **#3 ForwardedHeaders** — configured in `ServiceDefaults` + `UseAgentOsForwardedHeaders()` as first
  middleware in both hosts (`KnownIPNetworks`/`KnownProxies` cleared for ACA).
- ✅ **#4 Durable shared DataProtection** — `AddAgentOsDataProtection()`: Postgres-backed key ring
  (`config.data_protection_keys`, migration `AddDataProtectionKeys`) + shared `SetApplicationName("AgentOS")`;
  ephemeral fallback only when no DB (dev). Both hosts.
- ✅ **#9 Cloud Web → Production** — AppHost pins `Development` only in run mode; publish manifest now emits
  `ASPNETCORE_ENVIRONMENT=Production` for the Web.
- ✅ **#1 (partial)** — dropped the broken theme bind-mount + dev cache/header envs from the publish
  manifest; added `KC_PROXY=edge`. Run-mode stack byte-identical.
- ✅ **Web `appsettings.Production.json`** — `RequireDatabase=true` (fail-loud, no silent no-op repos).

**Still needs an `azd up` round-trip to verify (NOT shipped — can't validate without your Azure env):**
- #1 Keycloak durable storage (`KC_DB=postgres` + JDBC URL) + stable `KC_HOSTNAME` + theme-baked image.
- #6 realm `redirectUris`/`webOrigins` → real ACA Web FQDN (post-provision admin-API config).
- #2 rotate seed creds (`admin/admin`, `operator/operator`, client secret) via `azd env set` + strip seed users.
- MailHog/Email → real SMTP via azd secrets (MailHog still in the manifest; left to avoid run-mode ripple).

Blockers #5 (model id), #8 (`/mcp` auth), #10 (build_verifier RCE), #11 (tool policy), #12 (tenant
in pipeline) are **other batches** — not touched here.

## Verdict — deployable today?

**No.** A default `azd up` of `infra/AgentOs.AppHost` to Azure Container Apps comes up but is
functionally broken for any authenticated flow. Biggest blocker = **identity**: Keycloak ships as an
ephemeral ACA container (H2 storage, bind-mount to a non-existent path, realm `redirectUris` hardcoded
to `https://localhost:5180`), and no host calls `UseForwardedHeaders()` behind ACA's TLS-terminating
ingress → OIDC login impossible in cloud. Compounding: cloud Web pinned to `Development`, DataProtection
key ring ephemeral (encrypted tenant secrets + cookies break on every restart/scale), and default deploy
stands up `admin/admin` + committed seed users `operator/operator`, `member/member`. Even if it deployed
clean, the pipeline 404s on every Claude call — model id `claude-sonnet-4` is invalid.

## Deploy blockers (must-fix before prod)

| # | Issue | Location | Fix |
|---|-------|----------|-----|
| 1 | **Keycloak = ephemeral ACA container** — `.WithDataVolume()` + H2 (no `KC_DB`), no publish-mode exclusion, bind-mount to non-existent host path. Realm/users/bootstrap reset every revision. | `infra/AgentOs.AppHost/Program.cs:32-49` | Externalize to managed IdP (Entra ID / B2C) **or** persistent Keycloak (Postgres-backed, `KC_HOSTNAME`/`KC_PROXY=edge`, theme baked into image). Gate in-AppHost Keycloak to run-mode only. |
| 2 | **azd deploys seed/default creds to prod** — KC admin `admin/admin`, seed users `operator/operator` (realm-admin) + `member/member` (`temporary:false`), committed OIDC secret `agentic-web-dev-secret`. Account takeover of `default` tenant. | `infra/AgentOs.AppHost/appsettings.json:9-13`; `infra/keycloak/agentic-realm.json:78,145-172` | Gate `WithRealmImport`/seed users behind `ExecutionContext.IsRunMode`; require admin pw + client secret (no dev default) for cloud; strip seed users. |
| 3 | **No `UseForwardedHeaders`** — ACA forwards `X-Forwarded-Proto: https`; OIDC builds `http://` redirect_uri → KC rejects; secure cookie dropped. Absent repo-wide. | both hosts' `Program.cs` | Add `ForwardedHeadersOptions(XForwardedFor\|XForwardedProto)`, clear KnownNetworks/Proxies, `UseForwardedHeaders()` as **first** middleware. |
| 4 | **DataProtection key ring ephemeral + per-host** — bare `AddDataProtection()`. Encrypted tenant LLM keys/PATs become undecryptable on restart/scale; OIDC nonce + cookies break across replicas. | `Api/Program.cs:39`; `Web/Program.cs:43`; `EfAppConfigStore.cs:91,161` | `PersistKeysToAzureBlobStorage + ProtectKeysWithAzureKeyVault` (or DbContext) + shared `SetApplicationName("AgentOS")`. Wire in ServiceDefaults. |
| 5 | **Invalid model `claude-sonnet-4`** — passed verbatim as `ModelId`, no alias → HTTP 404 on every Requirement/Decomposer call. (`claude-sonnet-4-20250514` default also retires **2026-06-15**.) | `Api/appsettings.json:30`; `Web/appsettings.json:28`; `AgentsOptions.cs:18,33`; `PooledChatLlmClient.cs:92` | Set `claude-sonnet-4-6` in both appsettings + code defaults + `LlmOptions.cs:59`; keep `CostCalculator.cs:17` prefix in sync. |
| 6 | **Realm redirectUris/webOrigins hardcoded `https://localhost:5180`** — ACA host → `Invalid redirect_uri` on every cloud login. | `infra/keycloak/agentic-realm.json:81-82` | Parameterize `${ENV:default}` + inject real ACA Web FQDN, or set client post-import via admin API. |
| 7 | **KC `iss` ≠ `ValidIssuer`** — Authority bound to internal Aspire URL; `ValidateIssuer=true`, no `KC_HOSTNAME`. Every bearer 401s. | `AppHost/Program.cs:54-55,89-90`; `JwtAuthExtensions.cs:33,39` | `KC_HOSTNAME`=stable public URL + `KC_PROXY=edge`; both hosts' Authority → same public URL. |
| 8 | **`/mcp` has NO authorization** — anonymous callers drive the full 5-agent pipeline (uncapped spend) + read `default` tenant runs. Every sibling REST route is gated. | `Api/Program.cs:185-187`; `PipelineMcpTools.cs:22-82` | `.RequireAuthorization()` on `MapMcp("/mcp")` or global `RequireAuthenticatedUser` fallback. |
| 9 | **Cloud Web pinned `ASPNETCORE_ENVIRONMENT=Development`** — no prod exception handler, cookie `SecurePolicy=SameAsRequest`, ClientSecret guard disarmed. | `AppHost/Program.cs:84`; `Web/Program.cs:113-115,204-207` | Set Development only in `IsRunMode`; run cloud Web as Production; re-verify guards engage. |
| 10 | **`build_verifier` runs LLM-authored MSBuild in-process = RCE** — LLM can emit its own `.csproj/.targets` with `<Exec>`/malicious `<PackageReference>`; no `--no-restore`. Reachable via default-permissive policy (#11). | `Integration/BuildVerifier.cs:91-119`; `Tools/BuildVerifierTool.cs` | Locked-down ephemeral sandbox (no net, non-root, quotas); always synthesize the project, reject model `.csproj/.targets/.props`; `--no-restore` + locked feed. |
| 11 | **Tool policy default-permissive, no prod enforcement** — `Allow` unless per-tenant `tools/enforce=="true"`, never set anywhere. Governance moat is a no-op out of the box (build_verifier, runner_shell, all MCP). | `AppConfigToolPolicy.cs:34-45`; `ToolsModule.cs:33-34` | Fail-closed: default `tools/enforce` true, deny unless explicit allowlist; loud startup warning + safe seed allowlist. |
| 12 | **Pipeline resolves tenant `default` for ALL tenants in Web** — `InProcessPipelineClient` runs orchestrator in `Task.Run`; `HttpTenantContext` reads null HttpContext → `default`. Cross-tenant key use + commingled run history/cost. Fix (`AmbientIdentity.Push`) exists but omitted here. | `InProcessPipelineClient.cs:57`; `HttpTenantContext.cs:23-24`; `PipelineStudio.razor:566-580` | Capture tenant from `AuthenticationState`, `AmbientIdentity.Push` across the run; make RuntimeOverrides AmbientIdentity-aware. |

## Bugs (correctness / security, non-blocking deploy)

| Sev | Issue | Location | Fix |
|---|---|---|---|
| high | **MailHog dev SMTP shipped to prod**, no real provider; `verifyEmail:true` blocks signup | `AppHost/Program.cs:28-30,64-67`; `agentic-realm.json:11` | Run MailHog run-mode only; wire ACS/SendGrid into `Email__*` + realm `KC_SMTP_*`. |
| high | **Blazor circuits break on multi-replica ACA** — no min-replica/affinity, scoped UI state, no SignalR backplane | `AppHost/Program.cs:82-105` | Pin min 1 replica + sticky sessions; Azure SignalR for scale-out. |
| high | **`/settings` not admin-gated** — any member reads decrypted LLM keys/PAT, overwrites tenant secrets; Web Settings app not `AdminOnly` | `SettingsEndpoints.cs:19-52`; `AppCatalog.cs:48` | `.RequireAuthorization("Admin")` on all three; `AdminOnly:true` on Settings app. |
| high | **Admin can provision users into ANY tenant** — `tenantId` from route, no `ctx.TenantId` match | `TenantEndpoints.cs:47-91,127-183,363-413` | Add `ctx.TenantId==tenantId` Forbid guard; gate POST `/tenants` behind platform-superadmin. |
| high | **Missing `tenant` claim fails open to `default`** | `HttpTenantContext.cs:23-24` | Treat missing claim as unauthorized for tenant-scoped access. |
| high | **OrchestrationStore singleton cache shared across tenants** | `OrchestrationStore.cs:18-38`; `Program.cs:176` | Per-tenant keyed cache; tenant-explicit repo. |
| high | **RuntimeOverrides reads wrong tenant off HTTP-less LLM path** → per-tenant keys collapse to `default` | `RuntimeOverrides.cs:69-90`; `EfAppConfigStore.cs:40-57` | Resolve via `GetForTenantAsync`, thread tenant from caller. |
| high | **`AuthSession` reads identity/token from null HttpContext** (`prerender:false`) → "guest", null bearer → 401 in split mode | `AuthSession.cs:22-36`; `App.razor:15` | Derive from cascading `AuthenticationState`; capture token in OIDC `OnTokenValidated`. |
| med | **`MaxIterations<=0` injects null Code/Tests** behind non-nullable contract | `PipelineOrchestrator.cs:76,101-102,176-183` | `Math.Max(1, Math.Min(NMax, MaxIterations))` + `ValidateOnStart`. |
| med | **`EnableHumanInTheLoop` shipped but never read** (dead governance config) | `Api/appsettings.json:38`; `PipelineOptions.cs:17-18` | Implement gate or remove + `[Obsolete]`. |
| med | **`InMemoryMetricsCollector` singleton grows unbounded** (default path) | `InMemoryMetricsCollector.cs:14-33` | Per-run scope, drain after Snapshot, or cap+evict. |
| med | **Web `RequireDatabase=false`** — silent no-op repos = data loss if conn string missing in cloud | `Web/appsettings.json:40` | `true`; false only in `appsettings.Development.json`. |
| med | **`runner_shell` dispatch skips `IRemoteExecApprover`** (M4 path runs arbitrary shell) | `RemoteAgentTransport.cs:55-102` | Call `ApproveAsync` in `PushToolCallAsync`; refuse on denial. |
| med | **RemoteAgent CLI path bypasses `IToolGateway`** — no policy/evidence/audit | `IssueWorkAgent.cs:118-220`; `RemoteAgentLlmClient.cs:60-71` | Keep server in control via governed `runner_shell` loop, or replicate policy+evidence. |
| med | **CD smoke calls non-existent `/auth/token`** → reds verification; `/pipeline` payload `language` vs `Locale` | `.github/workflows/azd-deploy.yml:84-95,101` | KC client-credentials token; fix payload `Locale`; delete dead `Auth:Bearer`. |
| med | **`UseExceptionHandler("/Error")` but no `/Error` page** | `Web/Program.cs:206` | Add static-SSR `Error.razor`. |
| med | **`IssueWorkAgent.ParseOutcome` `LastIndexOf('{')`** mis-parses nested JSON → success reported as failure | `IssueWorkAgent.cs:242-255` | `IndexOf('{')` / reuse `JsonExtractor.ExtractBraced`. |
| med | **GraphExecutor iteration-cap bypass** on loop-only QA edge → re-bills to `HardStepCap=64` | `GraphExecutor.cs:76,132-167` | Force termination at `iter>=cap`; never pick loop/fail edge as fallback. |

## Backlog (medium/low — 37 items, highlights)

- **API container deployed but Web never calls it** (`Api:BaseUrl` unset → in-process); RemoteAgent hub only on API but dispatch from Web → runner features unreachable in cloud. **Decide topology.**
- Dev-machine runner can't reach cloud hub (no public URL, no WS affinity, in-memory backplane).
- Azure Postgres SSL/firewall/managed-identity not verified; no backup/DR runbook.
- **SSRF** via user-controlled `Host` in workspace connect/list/boards (IMDS `169.254.169.254`, internal ports) — `WorkspaceEndpoints.cs:58-71,95-131`.
- **Open redirect** on `/account/login?returnUrl` (no `IsLocalUrl`).
- Any member can revoke/close another member's runner/session (ownership not enforced).
- KC realm roles realm-wide not tenant-scoped; `NullKeycloakAdminClient` silent no-op when Admin BaseUrl missing.
- No observability backend wired (App Insights/OTLP conditional on env vars AppHost never sets).
- **LLM-gateway edge bugs:** PooledChatLlmClient wrapper leak + unbounded pool growth; `Temperature>1.0` Anthropic-400 no failover; `MafChatClient` ignores tenant overrides; `ApiKeyRouter` returns cooled key when all cooled; `FailoverLlmClient` only fails over on `LlmException` not 401/400/5xx; `CostCalculator` prices unknown models at `$0`.
- `*ForTenant` writes don't stamp `TenantId` (rely on caller).
- Branding/nits: 4 agent prompts say "Agentic SDLC" not "AgentOS"; `Web/appsettings.json:16` `Version`→`ApiVersion`; stale `Dockerfile.web` ("Agent Studio", missing modules); ViewAs dev cookie always `Secure=true`.
- **Docs-vs-reality:** README/CLAUDE/SECURITY document a non-existent `Auth:Mode=operator`/HS256 login; DEPLOYMENT/SETUP claim auto-CD on main (manual-dispatch only); DEPLOYMENT falsely says cloud Web boots on DevAutoLogin degraded path.

## Checked, NOT an issue

- RuntimeOverrides sync-over-async deadlock — refuted (singleton store, 15s cache, cache-hit inline, no SyncContext).
- Phantom-CSS in Web apps — clean (522 classes, all static `class=` tokens resolve).
- Provider name `"Anthropic"` — non-canonical but **alias-resolves** (not a blocker; tidy anyway).

## Ordered runbook to go live

1. **Decide identity topology** — managed IdP, or persistent Keycloak (`KC_DB=postgres`, baked theme, `KC_HOSTNAME`, `KC_PROXY=edge`, gate import/seed to run-mode). *(blocks all auth.)*
2. Fix AppHost run-vs-publish leaks (MailHog, `Email__SmtpHost=localhost`, Web `Development` pin → `IsRunMode`).
3. `UseForwardedHeaders` first in both hosts.
4. Durable DataProtection (Blob + Key Vault + shared app name) **before first prod boot**.
5. Parameterize realm redirectUris/webOrigins/secret → real ACA Web FQDN; align Authority/`ValidIssuer`.
6. Lock surfaces: `/mcp` auth, admin-gate `/settings`, tenant-match guard on tenant writes, fail-closed `tools/enforce`, sandbox `build_verifier`.
7. Fix model id + thread real tenant through in-process pipeline.
8. Set prod secrets via `azd env set` (full checklist in §Config).
9. Pin Web min-replicas=1 + sticky sessions (or Azure SignalR).
10. `azd up` (manual `workflow_dispatch` only — auto-CD is off).
11. Smoke: KC client-credentials token → authenticated `/pipeline`; **add Web `/health` probe**; fix `/pipeline` payload `Locale`.
12. Verify in running cloud app: OIDC login completes, run persists under real tenant, restart preserves saved LLM keys.
