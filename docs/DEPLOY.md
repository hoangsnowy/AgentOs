# Deploy quickstart

Đây là quickstart. Hướng dẫn chi tiết: [DEPLOYMENT.md](DEPLOYMENT.md). Bicep template: [infra/main.bicep](../infra/main.bicep). Workflow: [.github/workflows/deploy.yml](../.github/workflows/deploy.yml).

## Components

| File | Role |
|---|---|
| `Dockerfile` | Multi-stage (sdk:10.0 → aspnet:10.0), non-root uid 1654, HEALTHCHECK `/health`, EXPOSE 8080 |
| `infra/main.bicep` | ACR + Container Apps Env + Container App + Log Analytics + App Insights + Key Vault + UAMI |
| `infra/main.parameters.json` | Default params (region, sku, naming) |
| `.github/workflows/ci.yml` | Build + test trên PR + push |
| `.github/workflows/deploy.yml` | Build image → push ACR → update Container App (OIDC, `main` branch) |

## Local build image

```powershell
docker build -t agentic-sdlc-net:dev .
docker run -p 8080:8080 -e Llm__Claude__ApiKey="sk-ant-..." agentic-sdlc-net:dev
# → http://localhost:8080/health
```

**Note**: trong môi trường build prototype này Docker daemon offline nên chưa
verify `docker build` chạy được trên Dockerfile hiện tại. Dockerfile review
qua đọc — syntax OK, multi-stage clean, security non-root + HEALTHCHECK. Khi
chạy lần đầu nếu fail → kiểm tra `dotnet restore` step (cần lock file).

## Deploy lần đầu (Azure)

Xem [DEPLOYMENT.md §1-§3](DEPLOYMENT.md). Tóm tắt:

1. Az SP + OIDC federated credential cho GitHub.
2. `az deployment group create --template-file infra/main.bicep` lên resource group mới.
3. Push branch `main` → workflow auto build image → push ACR → update Container App.
4. Set LLM secret vào Key Vault: `Llm--Claude--ApiKey`, `Llm--AzureOpenAi--ApiKey`, `Llm--AzureOpenAi--Endpoint`.
5. Restart revision: `az containerapp revision restart`.

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
az keyvault purge --name <kv-name>   # vì soft-delete 7 ngày
```

## Status (Task 7 verification)

- ✓ Dockerfile committed (tracked at HEAD)
- ✓ infra/ committed (Bicep + params + README)
- ✓ workflows committed (ci.yml + deploy.yml)
- ⚠ `docker build` không chạy được trong env này (Docker Desktop daemon offline) — Dockerfile review-only
- ✓ `gh workflow list` → CI active (workflow run-able từ GitHub)
- ⏳ Live Azure deploy: chưa thử (cần Azure subscription + OIDC setup, ngoài scope)
