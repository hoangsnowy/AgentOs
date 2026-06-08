# Deployment — Azure Container Apps (via `azd` + Aspire)

Deployment is driven by **.NET Aspire + Azure Developer CLI (`azd`)**. The AppHost
(`infra/AgentOs.AppHost`) is the single source of truth for the topology — `azd` provisions
the whole app model (API + Web + Keycloak + managed Postgres + Container Apps env + ACR) from it.

---

## Prerequisites

- [Azure Developer CLI (`azd`)](https://aka.ms/azd) v1.9+ and [Azure CLI (`az`)](https://aka.ms/azcli)
- .NET 10 SDK
- Docker (azd builds container images locally before pushing to ACR)
- Azure subscription with permission to create resource groups + role assignments (Owner or
  Contributor + User Access Administrator — `azd` needs to assign roles to managed identities)

---

## Keycloak storage: H2 vs Postgres

**H2 (default — fine for dev/demo):**
Keycloak starts with an embedded H2 database. The realm (`agentic`) and seed users (`operator`,
`member`) are baked into the container image — they re-import automatically on every deploy.
H2 data survives while the container is running; it resets on `azd up` (new container image).
_Use this until you have real users signing up._

**Postgres (production — follow `docs/keycloak-prod-runbook.md`):**
User accounts created via the signup form survive across redeploys. Required when you have real
users. Involves creating a `keycloak` database on the provisioned Postgres server and running a
second `azd up` with the DB params set. See the runbook — it is intentionally a one-time
manual step after the first provision.

---

## A. Deploy from your machine

### A.1 — First deploy

```bash
az login                    # or: azd auth login
azd env new agentos-prod    # choose a name; sets AZURE_ENV_NAME
azd env set AZURE_LOCATION southeastasia  # or your preferred region
```

Set the required secrets — never edit the dev defaults in `appsettings.json`:

```bash
# Auth (required)
azd env set KeycloakAdminPassword    "$(openssl rand -base64 24)"
azd env set KeycloakWebClientSecret  "$(openssl rand -base64 32)"
```

**LLM keys are per-tenant, not a deploy secret.** AgentOS is bring-your-own-key: each workspace
(tenant) admin enters their own Claude / Azure OpenAI key in the running app (**Settings → API keys**),
stored encrypted per-tenant. You do **not** need to set a platform-wide LLM key to deploy — see
[Per-tenant LLM keys](#per-tenant-llm-keys) below.

> Optional, dev/demo only: setting `azd env set Llm__Claude__ApiKey "sk-ant-..."` installs a single
> shared platform key used **only as a fallback for tenants that have configured none**. For a real
> multi-tenant deploy leave it unset so every tenant must bring its own key (no shared spend, no
> cross-tenant key exposure).

Then deploy:

```bash
azd up   # provisions RG, ACR, ACA env, Postgres → builds + pushes images → deploys
```

`azd up` prints the **Web** and **API** URLs on completion.

### A.2 — Patch Keycloak redirect URIs (required for login to work)

The realm's `agentic-web` client has `redirectUris: https://localhost:5180/*` baked in. After
provision you need to update it to the real ACA Web FQDN:

```bash
RG=$(azd env get-values | sed -n 's/^AZURE_RESOURCE_GROUP=//p' | tr -d '"')
KC_FQDN=$(az containerapp show -n keycloak -g "$RG" --query properties.configuration.ingress.fqdn -o tsv)
WEB_FQDN=$(az containerapp show -n web -g "$RG" --query properties.configuration.ingress.fqdn -o tsv)

export KEYCLOAK_BASE_URL="https://$KC_FQDN"
export WEB_BASE_URL="https://$WEB_FQDN"
export KEYCLOAKADMINPASSWORD="$(azd env get-value KeycloakAdminPassword)"
export KEYCLOAKADMINUSERNAME="$(azd env get-value KeycloakAdminUsername)"

bash infra/hooks/postprovision.sh
```

### A.3 — Verify login works

1. Open `https://<WEB_FQDN>` in a browser.
2. Log in with `operator` / `operator` (seed password — `temporary: true`, so KC forces a reset).
3. Set a new password → you land on the AgentOS desktop.

### A.4 — Subsequent deploys

```bash
azd deploy        # re-build + re-push images only, no infra changes
# or
azd up            # provision + deploy (safe to re-run; Postgres and ACA env are idempotent)
```

Tear down when done:

```bash
azd down --purge
```

---

## B. GitHub Actions (CI → auto-deploy)

The committed workflow [`.github/workflows/azd-deploy.yml`](../.github/workflows/azd-deploy.yml)
runs `azd up` via OIDC (no client secrets stored in GitHub).

> **Auto-deploy is OFF by default.** The `on: workflow_dispatch` lets you trigger manually while you
> finish setting up the federated credential. Once ready, enable auto-CD per the comment in the file.

### B.1 — One-time setup (easiest)

```bash
azd pipeline config   # creates OIDC app reg + federated credential + sets all GH secrets/vars
```

### B.2 — Manual setup (if `azd pipeline config` isn't available)

```bash
APP_ID=$(az ad app create --display-name "agentos-github" --query appId -o tsv)
az ad sp create --id "$APP_ID"
SUB_ID=$(az account show --query id -o tsv)
az role assignment create --role Owner --assignee "$APP_ID" --scope "/subscriptions/$SUB_ID"

# Federated credential for the main branch
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:hoangsnowy/AgentOs:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

Set GitHub repo secrets + variables:

```bash
gh secret set AZURE_CLIENT_ID         --body "$APP_ID"
gh secret set AZURE_TENANT_ID         --body "$(az account show --query tenantId -o tsv)"
gh secret set AZURE_SUBSCRIPTION_ID   --body "$SUB_ID"
gh variable set AZURE_ENV_NAME        --body "agentos-prod"
gh variable set AZURE_LOCATION        --body "southeastasia"

# AgentOS secrets — injected into azd env before each deploy
gh secret set KEYCLOAKADMINPASSWORD    --body "$(openssl rand -base64 24)"
gh secret set KEYCLOAKWEBCLIENTSECRET  --body "$(openssl rand -base64 32)"
gh secret set LLM__CLAUDE__APIKEY      --body "sk-ant-..."
```

### B.3 — Workflow: inject secrets before `azd up`

The workflow needs a step before `azd up` to push secrets into the azd environment so they reach
the containers. Add this step after `azd login`:

```yaml
- name: Inject secrets into azd env
  run: |
    azd env set KeycloakAdminPassword    "${{ secrets.KEYCLOAKADMINPASSWORD }}"
    azd env set KeycloakWebClientSecret  "${{ secrets.KEYCLOAKWEBCLIENTSECRET }}"
    # LLM keys are per-tenant (set in-app via Settings → API keys). Only set a shared platform
    # fallback for a dev/demo deploy — leave unset for a real BYO-key multi-tenant deploy.
    if [ -n "${{ secrets.LLM__CLAUDE__APIKEY }}" ]; then
      azd env set Llm__Claude__ApiKey    "${{ secrets.LLM__CLAUDE__APIKEY }}"
    fi
    # Optional KC Postgres (after first deploy — see docs/keycloak-prod-runbook.md)
    if [ -n "${{ secrets.KEYCLOAKDBURL }}" ]; then
      azd env set KeycloakDbUrl      "${{ secrets.KEYCLOAKDBURL }}"
      azd env set KeycloakDbUsername "${{ secrets.KEYCLOAKDBUSERNAME }}"
      azd env set KeycloakDbPassword "${{ secrets.KEYCLOAKDBPASSWORD }}"
      azd env set KeycloakHostname   "${{ secrets.KEYCLOAKHOSTNAME }}"
    fi
```

After `azd up`, patch the KC redirect URIs automatically:

```yaml
- name: Patch KC redirect URIs
  run: |
    RG=$(azd env get-values | sed -n 's/^AZURE_RESOURCE_GROUP=//p' | tr -d '"')
    KC=$(az containerapp show -n keycloak -g "$RG" --query properties.configuration.ingress.fqdn -o tsv)
    WEB=$(az containerapp show -n web     -g "$RG" --query properties.configuration.ingress.fqdn -o tsv)
    export KEYCLOAK_BASE_URL="https://$KC"
    export WEB_BASE_URL="https://$WEB"
    export KEYCLOAKADMINPASSWORD="${{ secrets.KEYCLOAKADMINPASSWORD }}"
    bash infra/hooks/postprovision.sh
```

### B.4 — Turn on auto-deploy

```bash
gh variable set AZURE_DEPLOY_ENABLED --body true
```

Once set, every push to `main` that passes CI auto-deploys via the workflow.

---

## Per-tenant LLM keys

AgentOS is **bring-your-own-key (BYO)**, isolated per tenant — there is no required shared platform key.

**How a workspace sets its key:**
1. Sign in as a tenant **admin** (the `admin` realm role).
2. Open **Settings** (desktop) → **API keys** tab.
3. Paste the Anthropic / Azure OpenAI key + endpoint → **Save**.

The key is encrypted (DataProtection) and stored in `config.app_config`, keyed by `(TenantId, "Llm:Claude:ApiKey")`. It is durable across restarts and used **only** by that tenant's pipeline runs.

**Resolution + isolation guarantees:**
- When a tenant has its own key, **only that key** is used — the shared `appsettings`/env platform key is never appended, so one tenant can never spend on another's (or the platform's) key.
- The platform key (if set) is a fallback **only** for tenants that have configured none.
- A tenant with **no** key configured and no platform fallback gets a clear error ("No `<provider>` API key configured for your workspace…") pointing to Settings — never a silent run on someone else's key.
- The pipeline runs the orchestrator on a background thread; the caller's tenant is carried via `AmbientIdentity`, so key lookup, prompt overrides, budget, and run-history all resolve the signed-in tenant (never `default`).
- Settings is **admin-gated** in the component itself (not just hidden in the catalog) and writes are scoped to the signed-in tenant.

**Operator checklist:** deploy with no LLM key set → hand each tenant admin the Web URL → they self-serve their key in Settings. Verify isolation by signing in as two tenants and confirming each sees only its own key state.

## Production hardening (KC Postgres + real SMTP)

Follow [`docs/keycloak-prod-runbook.md`](keycloak-prod-runbook.md) for:

- Rotating seed users / stripping `operator`+`member` from the public realm
- KC Postgres (durable user accounts across redeploys)
- KC stable hostname (`KC_HOSTNAME`)
- Real SMTP (`Email__SmtpHost` / realm `smtpServer`)
- Multi-replica Web (min 1 replica + Azure SignalR backplane)

---

## Operating the deployment

```bash
azd env get-values           # lists all URLs, resource names, environment variables

# Logs
az containerapp logs show -n api -g <rg> --tail 100
az containerapp logs show -n web -g <rg> --tail 100

# Roll back (Container Apps keeps every revision)
az containerapp revision list     -n api -g <rg> -o table
az containerapp revision activate -n api -g <rg> --revision <name>
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Invalid redirect_uri` on login | KC client still points to localhost | Run `postprovision.sh` (A.2) |
| `Correlation failed` on OIDC callback | DataProtection key mismatch | Both API+Web must share `SetApplicationName("AgentOS")` — already wired in `ServiceDefaults` |
| 401 on API calls from Web | KC issuer mismatch | Set `KC_HOSTNAME` via `azd env set KeycloakHostname` then redeploy |
| Pipeline 404 on Claude calls | Wrong model id | `Llm:DefaultModel` must be `claude-sonnet-4-6` — check `appsettings.json` |
| Users lost after redeploy | KC on H2 (ephemeral) | Expected — follow KC Postgres runbook if you need persistent users |
| Container 503 after deploy | `/health` startup not ready | `az containerapp logs show -n <app> -g <rg>` — check DB connection string |
| `azd up` role assignment error | SP lacks permission | Grant Owner or Contributor + User Access Administrator |
