# Setup & First Run

A step-by-step guide to build and run AgentOS locally.

## 1. Install the .NET 10 SDK

Download from <https://dotnet.microsoft.com/download/dotnet/10.0>, choosing the x64 SDK (Windows / macOS / Linux).

Verify:

```bash
dotnet --list-sdks
# 10.0.100 [C:\Program Files\dotnet\sdk]
```

If the output has no line starting with `10.`, check `global.json` at the repo root (it pins `10.0.100`).

## 2. First build & test

From the `D:\LuanVan\prototype\` folder:

```bash
dotnet restore AgentOs.slnx
dotnet build  AgentOs.slnx --configuration Release
dotnet test   AgentOs.slnx --configuration Release
```

The suite is ~790+ tests; all should pass.

## 3. Configure LLM secrets (local)

Use .NET User Secrets so keys are never committed:

```bash
cd src/AgentOs.Api
dotnet user-secrets init
dotnet user-secrets set "Llm:Claude:ApiKey"  "sk-ant-..."
dotnet user-secrets set "Llm:AzureOpenAi:ApiKey" "..."
dotnet user-secrets set "Llm:AzureOpenAi:Endpoint" "https://<resource>.openai.azure.com"
```

Secrets are stored in `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`, **not in the repo**.

## 4. Run the API locally

```bash
cd src/AgentOs.Api
dotnet run
```

Browse to:

- Health: <https://localhost:5080/health>
- Scalar API Reference (UI): <https://localhost:5080/scalar/v1>
- OpenAPI spec (JSON): <https://localhost:5080/openapi/v1.json>

## 5. Configure GitHub Actions secrets (for deploy)

CI (`.github/workflows/ci.yml`) only restores, builds, and runs the test suite — no test
calls a real LLM, so **CI needs no LLM secrets**.

Secrets are only consumed by the optional azd deploy workflow
(`.github/workflows/azd-deploy.yml`). LLM keys there are per-tenant (set in-app via
**Settings → API keys**); a shared platform fallback is only needed for a dev/demo deploy.
In GitHub (**Settings → Secrets and variables → Actions → New repository secret**):

| Name | Value |
|---|---|
| `LLM__CLAUDE__APIKEY` | sk-ant-... (optional — only for a dev/demo deploy) |

When present, the deploy workflow runs `azd env set Llm__Claude__ApiKey` so the containers pick
it up. Leave it unset for a real BYO-key multi-tenant deploy.

## 6. Branch protection (recommended)

**Settings → Branches → Add rule:** `main`

- ☑ Require a pull request before merging
- ☑ Require status checks to pass before merging — select `Build & Test`
- ☑ Require linear history (rebase / squash merge only)

## 7. One-shot local dev — Aspire AppHost (Postgres + Keycloak + MailHog + API + Web)

`AgentOs.AppHost` is an Aspire AppHost: one `dotnet run` brings up every dev dependency in
containers (Postgres + Keycloak + MailHog) and starts the API + Blazor Web alongside them, with connection
strings + the OIDC authority wired across via Aspire service discovery — no docker-compose, no
hand-edited env vars.

```bash
dotnet run --project infra/AgentOs.AppHost
```

Open the Aspire dashboard URL printed in the console for live logs, traces, and the resource
graph (api, web, postgres, keycloak). Data volumes persist across restarts.

**Keycloak (multi-tenant auth)** auto-imports the `agentic` realm from
`infra/keycloak/agentic-realm.json` on first start:

- Admin console: the URL Aspire prints for the `keycloak` resource (admin / admin)
- Realm `agentic` — user registration enabled; clients `agentic-web` (auth-code) + `agentic-api`
  (bearer-only); realm roles `admin` / `member`; a `tenant` claim from the user attribute
- Seed user: `operator` / `operator` (tenant `default`, role `admin`)

`Auth__Keycloak__Authority` is injected by the AppHost, so the API runs as an OIDC resource server
out of the box (Keycloak OIDC is the only auth mode — there is no HS256/operator fallback). If you
run the API directly (without the AppHost), point `Auth:Keycloak:Authority` at any reachable
Keycloak realm; the standalone Web instead boots on `Auth:DevAutoLogin` (Development only).

Production runs Keycloak with an external DB and `start` (not `start-dev`); for that, point the
API/Web at a managed OIDC IdP and skip the AppHost-hosted Keycloak.

---

Once set up, see the [README](../README.md) for running the API, the AgentOS desktop, and the pipeline.
