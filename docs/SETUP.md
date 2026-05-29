# Setup & First Run

A step-by-step guide to build, run, and push the `agentos` repo to GitHub.

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
dotnet restore AgentOs.sln
dotnet build  AgentOs.sln --configuration Release
dotnet test   AgentOs.sln --configuration Release
```

Phase 1 has only 1 smoke test; the expected result is `Passed: 1`.

## 3. Configure LLM secrets (local)

Use .NET User Secrets so keys are never committed:

```bash
cd src/AgentOs.Api
dotnet user-secrets init
dotnet user-secrets set "Llm:Anthropic:ApiKey"  "sk-ant-..."
dotnet user-secrets set "Llm:AzureOpenAI:ApiKey" "..."
dotnet user-secrets set "Llm:AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com"
```

Secrets are stored in `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`, **not in the repo**.

## 4. Run the API locally

```bash
cd src/AgentOs.Api
dotnet run
```

Browse to:

- Health: <http://localhost:5080/health>
- Scalar API Reference (UI): <http://localhost:5080/scalar/v1>
- OpenAPI spec (JSON): <http://localhost:5080/openapi/v1.json>

## 5. Push to GitHub

The first time (in the `D:\LuanVan\prototype\` folder):

```bash
git init
git add .
git commit -m "chore: phase 1 — initial scaffold (.NET 10 solution + CI)"
git branch -M main

# Create the repo on GitHub (via the web or the gh CLI):
#   gh repo create agentos --public --description "A .NET-native multi-agent AI platform for the SDLC"
git remote add origin https://github.com/<your-username>/agentos.git
git push -u origin main
```

Check the **Actions** tab on GitHub — the CI workflow `.github/workflows/ci.yml` will run automatically the first time.

## 6. Configure GitHub Actions secrets (for CI to call the LLM)

In GitHub: **Settings → Secrets and variables → Actions → New repository secret**

| Name | Value |
|---|---|
| `ANTHROPIC_API_KEY` | sk-ant-... |
| `AZURE_OPENAI_ENDPOINT` | https://\<resource\>.openai.azure.com |
| `AZURE_OPENAI_API_KEY` | ... |

The CI workflow reads these secrets for the experimental tests that call a real LLM.

## 7. Branch protection (recommended)

**Settings → Branches → Add rule:** `main`

- ☑ Require a pull request before merging
- ☑ Require status checks to pass before merging — select `Build & Test`
- ☑ Require linear history (rebase / squash merge only)

## 8. One-shot local dev — Aspire AppHost (Postgres + Keycloak + API + Web)

`AgentOs.AppHost` is an Aspire AppHost: one `dotnet run` brings up every dev dependency in
containers (Postgres + Keycloak) and starts the API + Blazor Web alongside them, with connection
strings + the OIDC authority wired across via Aspire service discovery — no docker-compose, no
hand-edited env vars.

```bash
dotnet run --project infra/AgentOs.AppHost
```

Open the Aspire dashboard URL printed in the console for live logs, traces, and the resource
graph (api, web, postgres, keycloak). Data volumes persist across restarts.

**Keycloak (Epic D — multi-tenant auth)** auto-imports the `agentic` realm from
`infra/keycloak/agentic-realm.json` on first start:

- Admin console: the URL Aspire prints for the `keycloak` resource (admin / admin)
- Realm `agentic` — user registration enabled; clients `agentic-web` (auth-code) + `agentic-api`
  (bearer-only); realm roles `admin` / `member`; a `tenant` claim from the user attribute
- Seed user: `operator` / `operator` (tenant `default`, role `admin`)

`Auth__Mode=keycloak` + `Auth__Keycloak__Authority` are injected by the AppHost, so the API runs
as an OIDC resource server out of the box. If you run the API directly (without the AppHost),
set `Auth:Mode=operator` (the Phase-8 HS256 path) or point `Auth:Keycloak:Authority` at any
reachable Keycloak realm.

Production runs Keycloak with an external DB and `start` (not `start-dev`); for that, point the
API/Web at a managed OIDC IdP and skip the AppHost-hosted Keycloak.

---

Once set up, see the [README](../README.md) for running the API, the AgentOS desktop, and the pipeline.
