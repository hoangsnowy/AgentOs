# Deployment — Azure Container Apps (via `azd` + Aspire)

Deployment is driven by **.NET Aspire + Azure Developer CLI (`azd`)**. The AppHost
(`infra/AgentOs.AppHost`) is the single source of truth for the topology — `azd` provisions
the whole app model (API + Web + managed Postgres + Container Apps env + ACR + managed identity)
from it. No hand-written Bicep, no Dockerfile required. Topology details: [infra/README.md](../infra/README.md).

There are two ways in: **(A)** deploy from your machine (quickest way to *try* it), and
**(B)** GitHub Actions continuous delivery (auto-deploy when `main` goes green).

---

## Prerequisites

- [Azure Developer CLI (`azd`)](https://aka.ms/azd) and the [Azure CLI (`az`)](https://aka.ms/azcli)
- .NET 10 SDK
- An Azure subscription you can create resources in (the first `azd up` creates a resource group,
  ACR, Container Apps environment, Log Analytics, a Postgres flexible server, and a managed identity)

---

## A. Deploy from your machine (try it now)

```bash
az login                       # or: azd auth login
azd up                         # prompts: environment name, subscription, region → provisions + deploys
```

`azd up` prints the **API** and **Web** URLs when it finishes. Re-deploy after code changes with
`azd deploy` (or `azd up` again).

Set the LLM keys (stored as Container App env / Key Vault secrets), then redeploy:

```bash
azd env set Llm__Claude__ApiKey     "sk-ant-..."
azd env set Llm__AzureOpenAi__ApiKey   "..."
azd env set Llm__AzureOpenAi__Endpoint "https://<resource>.openai.azure.com"
azd deploy
```

> The Web still boots without keys on a degraded path (`Auth:DevAutoLogin` + the no-op
> repositories), so you can deploy first and add keys later.

Tear everything down when done:

```bash
azd down --purge
```

---

## B. Continuous delivery (GitHub Actions)

The committed workflow [`.github/workflows/azd-deploy.yml`](../.github/workflows/azd-deploy.yml)
runs `azd up` against your subscription. It triggers:

- **automatically** after the **CI** workflow succeeds on `main` — *but only* when the repo
  variable `AZURE_DEPLOY_ENABLED` is `true` (so `main` stays green before any Azure setup), and
- **manually** any time via **Actions → Deploy (azd / Aspire) → Run workflow**.

It authenticates with Azure over **OIDC** (no client secret stored in GitHub), runs `azd up`, then
smoke-tests `GET /health`, `POST /auth/token`, and a tiny `POST /pipeline` run.

### B.1 — Easiest: let `azd` wire the pipeline

From a local clone already linked to the GitHub repo:

```bash
azd pipeline config
```

This creates the OIDC app registration + federated credential, assigns the role, and sets the
GitHub repo **secrets** and **variables** below for you. Then do **B.3** (enable the gate).

### B.2 — Manual setup (if you don't run `azd pipeline config`)

```bash
# 1) App registration + service principal
APP_ID=$(az ad app create --display-name "agentos-github" --query appId -o tsv)
az ad sp create --id "$APP_ID"
SUB_ID=$(az account show --query id -o tsv)

# 2) Role — azd creates role assignments for the managed identity (ACR pull, Key Vault), so
#    Contributor is NOT enough. Use Owner (or Contributor + User Access Administrator).
az role assignment create --role Owner --assignee "$APP_ID" --scope "/subscriptions/$SUB_ID"

# 3) Federated credential — allow this repo's workflows on main (OIDC, no secret)
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:hoangsnowy/AgentOs:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

echo "AZURE_CLIENT_ID       = $APP_ID"
echo "AZURE_TENANT_ID       = $(az account show --query tenantId -o tsv)"
echo "AZURE_SUBSCRIPTION_ID = $SUB_ID"
```

Then set the repo **secrets** and **variables** (Settings → Secrets and variables → Actions, or
via `gh`):

| Kind | Name | Value |
|---|---|---|
| Secret | `AZURE_CLIENT_ID` | App ID from above |
| Secret | `AZURE_TENANT_ID` | Tenant ID |
| Secret | `AZURE_SUBSCRIPTION_ID` | Subscription ID |
| Variable | `AZURE_ENV_NAME` | e.g. `agentos-dev` |
| Variable | `AZURE_LOCATION` | e.g. `southeastasia` |

```bash
gh secret set AZURE_CLIENT_ID        --body "<app-id>"
gh secret set AZURE_TENANT_ID        --body "<tenant-id>"
gh secret set AZURE_SUBSCRIPTION_ID  --body "<sub-id>"
gh variable set AZURE_ENV_NAME       --body "agentos-dev"
gh variable set AZURE_LOCATION       --body "southeastasia"
# Optional: a known operator password the /pipeline smoke step logs in with
gh secret set AZD_OPERATOR_PASSWORD  --body "<password>"
```

### B.3 — Turn auto-deploy on

```bash
gh variable set AZURE_DEPLOY_ENABLED --body true
```

Until this is `true`, pushes to `main` will NOT auto-deploy (the deploy job is skipped). A manual
**Run workflow** still works regardless, so you can try a one-off deploy before flipping it.

### B.4 — Try it

- **Manual:** Actions → *Deploy (azd / Aspire)* → **Run workflow** (on `main`).
- **Automatic:** once `AZURE_DEPLOY_ENABLED=true`, merge to `main` → CI runs → on success the deploy
  workflow runs `azd up` and smoke-tests the result.

---

## Operating the deployment

Resource names come from your `AZURE_ENV_NAME`; `azd env get-values` lists URLs + names.

```bash
# Logs
az containerapp logs show -n <api-app> -g <rg> --tail 100

# Roll back to a previous revision (Container Apps keeps every revision)
az containerapp revision list     -n <api-app> -g <rg> -o table
az containerapp revision activate -n <api-app> -g <rg> --revision <name>

# Cost: each LLM call is logged with its USD cost; query App Insights `traces`, or use the
# in-app admin Cost view (Desktop → Cost) once runs exist.
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Deploy job didn't run on push to `main` | gate off | `gh variable set AZURE_DEPLOY_ENABLED --body true` |
| `azd auth login` fails in CI | OIDC subject/role | Federated subject must match `repo:hoangsnowy/AgentOs:ref:refs/heads/main`; role must be Owner / +User Access Administrator |
| `azd up` fails creating role assignments | SP lacks permission | Grant **Owner** or **User Access Administrator** (Contributor alone can't assign roles) |
| Container App 503 after deploy | `/health` not ready | `az containerapp logs show …` — check startup + DB connection |
| LLM call returns 401 in cloud | key not set | `azd env set Llm__Claude__ApiKey …` then `azd deploy` |
