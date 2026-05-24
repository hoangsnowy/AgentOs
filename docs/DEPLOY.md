# Deploy quickstart

> ⚠️ **Superseded.** Deployment moved to .NET Aspire + `azd` — see [infra/README.md](../infra/README.md).
> The Bicep + Docker GitHub Actions flow below was removed; this page is kept for historical reference only.

## Components

| File | Role |
|---|---|
| `Dockerfile` | Multi-stage (sdk:10.0 → aspnet:10.0), non-root uid 1654, HEALTHCHECK `/health`, EXPOSE 8080 |
| `infra/main.bicep` | ACR + Container Apps Env + Container App + Log Analytics + App Insights + Key Vault + UAMI |
| `infra/main.parameters.json` | Default params (region, sku, naming) |
| `.github/workflows/ci.yml` | Build + test on PR + push |
| `.github/workflows/deploy.yml` | Build image → push ACR → update Container App (OIDC, `main` branch) |

## Local build image

```powershell
docker build -t agentic-sdlc-net:dev .
docker run -p 8080:8080 -e Llm__Claude__ApiKey="sk-ant-..." agentic-sdlc-net:dev
# → http://localhost:8080/health
```

**Note**: in this prototype build environment the Docker daemon is offline, so it has not
been verified that `docker build` succeeds against the current Dockerfile. The Dockerfile was
reviewed by reading — syntax OK, multi-stage clean, security non-root + HEALTHCHECK. If it
fails on the first run → check the `dotnet restore` step (a lock file is required).

## First-time deploy (Azure)

See [DEPLOYMENT.md §1-§3](DEPLOYMENT.md). Summary:

1. Az SP + OIDC federated credential for GitHub.
2. `az deployment group create --template-file infra/main.bicep` into a new resource group.
3. Push the `main` branch → the workflow auto-builds the image → pushes to ACR → updates the Container App.
4. Set the LLM secrets in Key Vault: `Llm--Claude--ApiKey`, `Llm--AzureOpenAi--ApiKey`, `Llm--AzureOpenAi--Endpoint`.
5. Restart the revision: `az containerapp revision restart`.

## Verify

```bash
APP_URL=$(az containerapp show -n agenticsdlc-dev -g rg-Hoang-LuanVan --query properties.configuration.ingress.fqdn -o tsv)
curl https://$APP_URL/health
# → {"status":"Healthy","utc":"..."}
curl -X POST https://$APP_URL/requirement -H "Content-Type: application/json" \
  -d '{"description":"Test story","locale":"vi-VN"}'
```

## Cleanup

```bash
az group delete --name rg-Hoang-LuanVan --yes --no-wait
az keyvault purge --name <kv-name>   # because of the 7-day soft-delete
```

## Status (Task 7 verification)

- ✓ Dockerfile committed (tracked at HEAD)
- ✓ infra/ committed (Bicep + params + README)
- ✓ workflows committed (ci.yml + deploy.yml)
- ⚠ `docker build` could not run in this env (Docker Desktop daemon offline) — Dockerfile review-only
- ✓ `gh workflow list` → CI active (workflow run-able from GitHub)
- ⏳ Live Azure deploy: not attempted (requires an Azure subscription + OIDC setup, out of scope)
