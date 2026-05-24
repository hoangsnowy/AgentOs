# Infra — Azure Container Apps deployment

## Stack

| Resource | SKU | Mục đích |
|---|---|---|
| Azure Container Registry | Basic | Lưu image Docker |
| Container Apps Environment | Consumption | Host Container App (auto-scale, scale-to-zero) |
| Container App | — | Run `AgenticSdlc.Api` |
| Log Analytics Workspace | PerGB2018 | Centralized log + diagnostic |
| Application Insights | Workspace-based | APM, distributed trace, agent cost log |
| Key Vault | Standard | Lưu Anthropic + Azure OpenAI API key |
| User-Assigned Identity | — | ACR pull + Key Vault Secrets read |

## Deploy thủ công (lần đầu)

```bash
# 1. Tạo resource group
az group create --name rg-Hoang-LuanVan --location southeastasia

# 2. Deploy Bicep
az deployment group create \
  --resource-group rg-Hoang-LuanVan \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json \
  --parameters containerImage=mcr.microsoft.com/azuredocs/containerapps-helloworld:latest

# (lần đầu dùng image placeholder — bước build sau sẽ update)

# 3. Build + push image lên ACR
ACR=$(az acr list -g rg-Hoang-LuanVan --query "[0].loginServer" -o tsv)
az acr login --name "$ACR"
docker build -t "$ACR/agenticsdlc:$(git rev-parse --short HEAD)" .
docker push "$ACR/agenticsdlc:$(git rev-parse --short HEAD)"

# 4. Update Container App với image thật
az containerapp update \
  --name agenticsdlc-dev \
  --resource-group rg-Hoang-LuanVan \
  --image "$ACR/agenticsdlc:$(git rev-parse --short HEAD)"

# 5. Set LLM secret vào Key Vault
KV=$(az deployment group show -g rg-Hoang-LuanVan -n main --query "properties.outputs.keyVaultUri.value" -o tsv | sed 's|https://||;s|/||')
az keyvault secret set --vault-name "$KV" --name "Llm--Anthropic--ApiKey" --value "sk-ant-..."
az keyvault secret set --vault-name "$KV" --name "Llm--AzureOpenAI--ApiKey" --value "..."
az keyvault secret set --vault-name "$KV" --name "Llm--AzureOpenAI--Endpoint" --value "https://<resource>.openai.azure.com"

# 6. Restart Container App để pickup secret mới
az containerapp revision restart --name agenticsdlc-dev --resource-group rg-Hoang-LuanVan
```

## Deploy tự động qua GitHub Actions

Sau khi setup OIDC federated credential (xem `.github/workflows/deploy.yml`),
mỗi push lên `main` tự build + push image + update Container App revision.

## Cleanup

```bash
az group delete --name rg-Hoang-LuanVan --yes --no-wait
```

> ⚠️ Key Vault soft-delete giữ 7 ngày. Để xoá hẳn:
> `az keyvault purge --name <kv-name>`

## Cost estimate (Q2/2026, southeastasia)

| Resource | Free / Min cost / Tháng |
|---|---|
| Container Apps (Consumption, idle/0 replica) | ~$0 (chỉ trả request thực tế) |
| Container Apps (1 replica 0.5 CPU 1GB chạy liên tục) | ~$15-20 |
| Log Analytics (5GB free + ingest) | ~$0-3 |
| App Insights (5GB free) | ~$0 |
| ACR Basic | ~$5 |
| Key Vault Standard | ~$0.03 / 10k ops |
| **Tổng prototype dev (scale-to-zero)** | **~$5-10/tháng** |

Khi có demo / load test, cost LLM (Anthropic + Azure OpenAI) sẽ vượt xa cost infra.
