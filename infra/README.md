# Infra — Azure deploy via azd (Aspire)

Deployment is driven by **.NET Aspire + Azure Developer CLI (`azd`)**. The AppHost
(`infra/AgentOs.AppHost`) is the single source of truth for the topology; `azd`
provisions everything from it:

| Resource | From the app model |
|---|---|
| Container Apps Environment + Log Analytics | implicit (Aspire) |
| Azure Container Registry | implicit (image push) |
| Container App: API | `AddProject("api")` |
| Container App: Web (AgentOS desktop) | `AddProject("web")` |
| Azure Database for PostgreSQL flexible | `AddAzurePostgresFlexibleServer("postgres")` |
| Managed identity + role assignments | implicit (Aspire) |

> The old hand-written `infra/main.bicep` + `.github/workflows/deploy.yml` (Docker build +
> `az containerapp`) were **removed** — `azd` replaces them. `azd` builds container images
> via the .NET SDK (no Dockerfile needed); `Dockerfile`/`Dockerfile.web` are kept only for
> standalone `docker build`.

## First deploy (local)

```bash
azd auth login
azd up                 # prompts for env name + subscription + region, then provisions + deploys
```

`azd up` prints the API + Web URLs. Re-deploy after code changes: `azd deploy` (or `azd up`).

## LLM secrets

```bash
azd env set Llm__Claude__ApiKey   "sk-ant-..."
azd env set Llm__AzureOpenAi__ApiKey "..."
azd env set Llm__AzureOpenAi__Endpoint "https://<resource>.openai.azure.com"
azd deploy
```

(The Web still boots without keys on a degraded path — `Auth:DevAutoLogin` + the no-op
repositories — so you can deploy first and add keys later.)

## CI/CD (GitHub Actions)

Easiest: `azd pipeline config` — wires OIDC + the repo secrets/variables and a workflow.
The committed [`.github/workflows/azd-deploy.yml`](../.github/workflows/azd-deploy.yml) runs
`azd up` on push to `main`. It needs:

- secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (existing OIDC app)
- variables `AZURE_ENV_NAME`, `AZURE_LOCATION`

⚠️ The OIDC service principal needs **Owner** or **User Access Administrator** — `azd`
creates role assignments for the managed identity (ACR pull, Key Vault). Contributor alone fails.

## Local dev (run everything)

```bash
azd auth login            # or just run the AppHost without Azure
dotnet run --project infra/AgentOs.AppHost
```

The AppHost runs Postgres + Keycloak + MailHog as local containers + API + Web + the Aspire
dashboard, with connection strings and the OIDC authority wired automatically.

## Cleanup

```bash
azd down --purge
```
