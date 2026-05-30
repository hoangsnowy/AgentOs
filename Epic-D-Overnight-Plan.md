# Epic D — Overnight Autopilot Plan

> **OIDC web signup + multi-tenant Keycloak**
> Branch: `feat/web-oidc-signup` · HEAD at plan time: `5226f57`
> Generated: 2026-05-30 · Time budget: ~12h (overnight → morning)
> Mode: **AUTOPILOT — no user wake-up, no AskUserQuestion**

---

## State at plan time

| Check | Value |
|-------|-------|
| HEAD | `5226f57` feat(auth): Web OIDC client + public sign-up, drop operator JWT |
| Worktree | clean (no WIP) |
| Origin branch | **not pushed yet** (Task H = first push) |
| Open PR | none |
| Parallel branch | `feat/multi-tenant-keycloak` — fully merged in (web-oidc 13 ahead / 0 behind) |

### Already shipped (do NOT redo)
- OIDC Authorization Code + PKCE, confidential `agentic-web` client, cookie+OIDC.
- API as `bearerOnly` resource server (`agentic-api`), JWT bearer + Admin/Member policies.
- Tenant resolution via JWT `tenant` claim → `HttpTenantContext`, row-level isolation.
- `KeycloakAdminClient.CreateUserAsync` + admin token flow.
- Aspire AppHost: Postgres + Keycloak (realm auto-import) + API + Web, single-F5.
- Realm `agentic`: `registrationAllowed:true`, `resetPasswordAllowed:true`, 2 clients, 2 roles.
- Tests: `KeycloakAdminClientTests`, `TenantIsolationTests`, `TenantsRepositoryTests`, `TestTenantContext`.

---

## Autopilot rules (read before every task)

1. **Per task loop**: implement → `dotnet build` → `dotnet test` (scoped) → `git commit` → `git push origin feat/web-oidc-signup`.
2. **Commit format**: Conventional Commits, ≤50 char subject. `feat(auth):`, `fix(security):`, `test(auth):`, `docs(auth):`. Body only when *why* non-obvious.
3. **Red build/test**: fix if inside current task scope. If fix needs work outside this task → **STOP that commit**, append entry to `OVERNIGHT_BLOCKERS.md` (task id, error verbatim, what's needed, what was tried), move to next task.
4. **NO `AskUserQuestion`.** Every small decision → pick sensible default, record it in PR body "Decisions" list.
5. **Time budget ~12h.** If exceeded → STOP, leave Task I (docs) undone, ensure Task H (PR) done.
6. **Order**: A → B → C → D → E → F → G → H. Task I optional, only if budget remains.
7. **Push after every task** so morning check shows incremental progress.
8. **Never** `--no-verify`, never force-push, never touch `main`.
9. Secrets: never commit a real secret. Dev placeholders go in `appsettings.Development.json` (gitignored if not already — verify).

---

## Task A — Frontend Blazor signup UI  ·  HIGH · ~2h

**Goal**: custom Blazor signup replacing Keycloak built-in registration page.

- New: `src/AgentOs.Web/Components/Pages/Account/Signup.razor` + code-behind.
- New API endpoint: `POST /api/account/signup` (in `AgentOs.Modules.Identity` or a Web minimal API) → calls `KeycloakAdminClient.CreateUserAsync` + tenant assignment (see Task C — A can stub tenant assign, C completes it).
- Form fields: email, password, display name, optional tenant slug.
- Validation:
  - email format (`EmailAddressAttribute`)
  - password ≥12 chars, must contain upper + lower + digit + symbol
  - tenant slug regex `^[a-z0-9]([a-z0-9-]{1,30}[a-z0-9])?$` (lowercase, hyphen-internal)
- Disable Keycloak self-registration page in realm later (Task D toggles realm flag `registrationAllowed:false` once custom flow proven — **default decision: keep `registrationAllowed:true` until Task C green, then flip in Task D**).
- **DoD**: signup end-to-end through Blazor form, user created in Keycloak, no Keycloak login/registration UI involved for signup.

---

## Task B — Email verification flow  ·  HIGH · ~1.5h  (fixes red flag #1)

- Set `verifyEmail: true` in `infra/keycloak/agentic-realm.json`.
- Add **MailHog** to Aspire AppHost as dev SMTP (`builder.AddContainer("mailhog", "mailhog/mailhog")` with SMTP 1025 / UI 8025; wire Keycloak `KC_SMTP_*` env or realm `smtpServer` block pointing at it).
- Realm `smtpServer`: host=mailhog, port=1025, from=`noreply@agentic.local`. Prod = env-based (`Auth__Keycloak__Smtp__*`), no hardcoded literals.
- **DoD**: new user with unverified email cannot complete login; clicking verify link in MailHog flips user to active and login succeeds.

---

## Task C — Self-signup → tenant auto-assignment  ·  HIGH · ~2h

Three signup modes, dispatched by signup request shape:

1. **Slug-based**: user supplies tenant slug → create tenant if absent (first user becomes tenant **Admin**); else reject unless invite present.
2. **Invite-based**: `?invite=<token>` query → validate token → join existing tenant with role from invite.
3. **Auto-create**: no slug, no invite → create a fresh single-user tenant, user is its Admin.

- Persist `tenant` claim onto the Keycloak user (user attribute) so the `agentic-web` protocol mapper emits it on next token.
- Tenant + membership rows written transactionally with Keycloak user create (compensate/delete Keycloak user if DB write fails — record this saga decision in PR).
- **DoD**: 3 integration tests (one per mode) pass; token after signup carries correct `tenant` claim.

---

## Task D — Security hardening  ·  HIGH · ~1h  (fixes red flags #2–#5)

- `agentic-realm.json`: `agentic-web` → `directAccessGrantsEnabled: false` (kill ROPC on code-flow client).
- `RequireHttpsMetadata`: drive from config, **default true**, only `false` in `appsettings.Development.json`.
- Cookie `Secure`: `Always` in non-Development, `SameAsRequest` in Development.
- Move dev secrets out of code:
  - `agentic-web-dev-secret` → `appsettings.Development.json` (`Auth:Keycloak:ClientSecret`).
  - admin `admin/admin` → AppHost parameters / dev config, no literal fallback in `.cs`.
  - Replace `?? "literal"` fallbacks with config-required + clear startup exception if missing in non-dev.
- If Task A proven: flip realm `registrationAllowed: false`.
- **DoD**: `grep -rn "agentic-web-dev-secret\|admin/admin" --include=*.cs` returns nothing outside dev config files; build green.

---

## Task E — Tenant admin UI  ·  MED · ~2h

- `Components/Pages/TenantAdmin/Members.razor`: list current tenant members, invite new (email → create invite token + send email + optionally pre-create user).
- `Components/Pages/TenantAdmin/Settings.razor`: tenant name, slug (read-only after create), role list.
- Authorization: page-level policy requiring `Admin` role **scoped to the acting tenant** (reuse `ITenantContext`).
- **DoD**: tenant Admin invites a member; member clicks email link → becomes active member of that tenant.

---

## Task F — Audit / activity log  ·  MED · ~1h

- New entity + table `AuditEvents`: `Id, UserId, TenantId, Action, Target, IpAddress, UserAgent, TimestampUtc`.
- Log mutations: signup, invite, role change, tenant create, login failure.
- EF migration added (per-module DbContext convention — put in the owning module).
- Dashboard "Audit" tab (tenant Admin only) lists events for the tenant, newest first, paged.
- **DoD**: each tracked action writes a row capturing who / what / when / from where; tab renders them.

---

## Task G — Test coverage Epic D  ·  MED · ~1.5h

- Integration (E2E-ish): signup → verify email → login → access protected resource → invite member → logout.
- Unit: password/email/slug validation rules; tenant resolution edge cases (missing claim, stale token, principal with multiple tenant claims).
- **DoD**: ≥15 new tests, all green (`dotnet test` for `AgentOs.Tests`; E2E in `AgentOs.E2E.Tests` if Keycloak container available, else mark `[Trait("Category","RequiresKeycloak")]` and skip in unit run — record skip in log, not silent).

---

## Task H — Open PR + verify CI  ·  HIGH · ~30min

- `git push -u origin feat/web-oidc-signup` (first push — branch not yet on origin).
- `gh pr create --base main --head feat/web-oidc-signup --draft` with body:
  - **Summary**: Epic D — OIDC web signup + multi-tenant Keycloak.
  - **Security checklist**: 5 red flags fixed (B #1, D #2/#4/#5, D #3).
  - **Decisions**: every autopilot default chosen.
  - **References**: thesis Ch2.2 (multi-tenant), Ch3.2 (security policies).
- Wait CI (build + test + sonarcloud if configured). If red → fix in scope or log blocker.
- When green → `gh pr ready` on the new PR number.
- **DoD**: PR exists, CI green, marked ready for review.

---

## Task I — Documentation  ·  LOW · ~1h  (optional, budget permitting)

- `docs/auth/oidc-flow.md`: OIDC + PKCE sequence diagram (mermaid).
- `docs/auth/tenant-resolution.md`: 3 strategies, chosen one + why.
- `docs/auth/security-hardening.md`: prod deploy checklist.
- `README.md`: link the 3 docs.
- **DoD**: README references all three.

---

## Estimated total

| Tasks | Hours |
|-------|-------|
| A–D (HIGH core) | 6.5h |
| E–G (MED) | 4.5h |
| H (PR) | 0.5h |
| **Critical path A–H** | **~11.5h** |
| I (optional docs) | +1h |

Fits the ~12h overnight window with Task I as stretch. If anything slips, sacrifice I, never H.

---

## Morning checklist for user

1. `git log --oneline main..feat/web-oidc-signup` — count commits landed (target: ~8, one per task).
2. Open PR — read Decisions + Security checklist.
3. `OVERNIGHT_BLOCKERS.md` — exists only if something was deferred; read it first.
4. CI status on the PR.
