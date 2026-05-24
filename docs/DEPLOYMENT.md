# Deployment — Azure Container Apps

Hướng dẫn deploy `AgenticSdlc.Api` lên Azure Container Apps qua Bicep + GitHub Actions.

## 1. One-time setup (Azure side)

### 1.1 Service Principal + OIDC federated credential

GitHub Actions dùng OIDC token (KHÔNG cần client secret lưu trong GH).

```bash
# Tạo Azure AD application
APP_ID=$(az ad app create --display-name "agentic-sdlc-net-github" --query appId -o tsv)
SP_ID=$(az ad sp create --id "$APP_ID" --query id -o tsv)

# Gán role Contributor lên subscription (hoặc resource group nếu muốn hẹp hơn)
SUB_ID=$(az account show --query id -o tsv)
az role assignment create --role Contributor --assignee "$APP_ID" --scope "/subscriptions/$SUB_ID"

# Federated credential — chỉ cho phép workflow trên branch main (hoặc env tag)
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<your-org>/agentic-sdlc-net:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Lấy info cho GH secret
echo "AZURE_CLIENT_ID = $APP_ID"
echo "AZURE_TENANT_ID = $(az account show --query tenantId -o tsv)"
echo "AZURE_SUBSCRIPTION_ID = $SUB_ID"
```

### 1.2 GitHub repository secret

**Settings → Secrets and variables → Actions → New repository secret**:

| Tên | Giá trị |
|---|---|
| `AZURE_CLIENT_ID` | App ID từ bước trên |
| `AZURE_TENANT_ID` | Tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID |

### 1.3 First-time infrastructure deployment

Workflow `deploy.yml` cần resource group + ACR có sẵn. Lần đầu tạo thủ công:

```bash
az group create --name rg-Hoang-LuanVan --location southeastasia

az deployment group create \
  --resource-group rg-Hoang-LuanVan \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json \
  --parameters containerImage=mcr.microsoft.com/azuredocs/containerapps-helloworld:latest
```

Sau đó set LLM secret vào Key Vault (xem `infra/README.md` bước 5).

## 2. Subsequent deployments

Mỗi push lên `main` (không touch `docs/` hoặc `.md`) tự trigger workflow:

1. Run `dotnet test` Release.
2. Login Azure (OIDC).
3. Build Docker image, push lên ACR với tag = `${{ github.sha }}`.
4. Re-apply Bicep với image tag mới → Container App revision update.
5. Print FQDN của container app.

Manual trigger qua **Actions → Deploy to Azure Container Apps → Run workflow**, chọn environment `dev|staging|prod`.

## 3. Smoke test post-deploy

```bash
FQDN=$(az containerapp show -n agenticsdlc-dev -g rg-Hoang-LuanVan --query "properties.configuration.ingress.fqdn" -o tsv)

curl "https://$FQDN/health"
# → {"status":"Healthy","utc":"..."}

curl -X POST "https://$FQDN/requirement" \
  -H "Content-Type: application/json" \
  -d '{"description":"Hệ thống quản lý sản phẩm","nMax":3}'
```

## 4. Roll back

Container Apps lưu tất cả revision. Roll back qua:

```bash
# List revisions
az containerapp revision list -n agenticsdlc-dev -g rg-Hoang-LuanVan \
  --query "[].{name:name, active:properties.active, image:properties.template.containers[0].image, created:properties.createdTime}" \
  -o table

# Activate revision cũ
az containerapp revision activate \
  --name <revision-name> \
  --resource-group rg-Hoang-LuanVan \
  -n agenticsdlc-dev
```

## 5. Monitor

- **Logs**: `az containerapp logs show -n agenticsdlc-dev -g rg-Hoang-LuanVan --tail 100`
- **Application Insights**: <https://portal.azure.com> → Application Insights → `agenticsdlc-ai-dev`
- **Live metrics**: Application Insights → Live Metrics
- **Cost log**: query KQL trong App Insights:
  ```kusto
  traces
  | where message contains "RequirementAgent done" or message contains "CodingAgent done"
  | extend cost = todouble(extract("\\$([0-9.]+)", 1, message))
  | summarize total_cost = sum(cost), n = count() by bin(timestamp, 1h)
  ```

## 6. Cleanup

```bash
az group delete --name rg-Hoang-LuanVan --yes --no-wait
# Key Vault soft-delete → purge sau 7 ngày hoặc chủ động:
# az keyvault purge --name <kv-name>
```

## Troubleshooting

| Triệu chứng | Nguyên nhân thường gặp | Fix |
|---|---|---|
| Workflow fail tại `az containerapp update` | ACR chưa setup | Chạy bước 1.3 thủ công lần đầu |
| Container app trả 503 sau deploy | Probe `/health` timeout | Check log: `az containerapp logs show ...` |
| LLM call trả 401 từ Container App | Secret Key Vault chưa set | Set qua `az keyvault secret set ...` + restart revision |
| Cost App Insights tăng đột biến | Verbose log level | Reduce `Logging:LogLevel:Default` về `Warning` |
