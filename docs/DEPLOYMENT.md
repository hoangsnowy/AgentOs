# Deployment — Azure Container Apps

> ⚠️ **Superseded.** Deployment moved to .NET Aspire + `azd` — see [infra/README.md](../infra/README.md).
> The Bicep + GitHub Actions flow below was removed; this page is kept for historical reference only.

## 1. One-time setup (Azure side)

### 1.1 Service Principal + OIDC federated credential

GitHub Actions uses an OIDC token (NO client secret stored in GH).

```bash
# Create the Azure AD application
APP_ID=$(az ad app create --display-name "agentos-github" --query appId -o tsv)
SP_ID=$(az ad sp create --id "$APP_ID" --query id -o tsv)

# Assign the Contributor role on the subscription (or the resource group for a narrower scope)
SUB_ID=$(az account show --query id -o tsv)
az role assignment create --role Contributor --assignee "$APP_ID" --scope "/subscriptions/$SUB_ID"

# Federated credential — only allow workflows on the main branch (or an env tag)
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<your-org>/agentos:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Get the info for the GH secrets
echo "AZURE_CLIENT_ID = $APP_ID"
echo "AZURE_TENANT_ID = $(az account show --query tenantId -o tsv)"
echo "AZURE_SUBSCRIPTION_ID = $SUB_ID"
```

### 1.2 GitHub repository secrets

**Settings → Secrets and variables → Actions → New repository secret**:

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | App ID from the step above |
| `AZURE_TENANT_ID` | Tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID |

### 1.3 First-time infrastructure deployment

The `deploy.yml` workflow needs an existing resource group + ACR. Create them manually the first time:

```bash
az group create --name rg-Hoang-LuanVan --location southeastasia

az deployment group create \
  --resource-group rg-Hoang-LuanVan \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json \
  --parameters containerImage=mcr.microsoft.com/azuredocs/containerapps-helloworld:latest
```

Then set the LLM secrets in Key Vault (see `infra/README.md` step 5).

## 2. Subsequent deployments

Every push to `main` (that does not touch `docs/` or `.md`) automatically triggers the workflow:

1. Run `dotnet test` Release.
2. Log in to Azure (OIDC).
3. Build the Docker image, push to ACR with tag = `${{ github.sha }}`.
4. Re-apply Bicep with the new image tag → Container App revision update.
5. Print the container app FQDN.

Trigger manually via **Actions → Deploy to Azure Container Apps → Run workflow**, choosing the environment `dev|staging|prod`.

## 3. Smoke test post-deploy

```bash
FQDN=$(az containerapp show -n agenticsdlc-dev -g rg-Hoang-LuanVan --query "properties.configuration.ingress.fqdn" -o tsv)

curl "https://$FQDN/health"
# → {"status":"Healthy","utc":"..."}

curl -X POST "https://$FQDN/requirement" \
  -H "Content-Type: application/json" \
  -d '{"description":"Product management system","nMax":3}'
```

## 4. Roll back

Container Apps keeps every revision. Roll back via:

```bash
# List revisions
az containerapp revision list -n agenticsdlc-dev -g rg-Hoang-LuanVan \
  --query "[].{name:name, active:properties.active, image:properties.template.containers[0].image, created:properties.createdTime}" \
  -o table

# Activate the old revision
az containerapp revision activate \
  --name <revision-name> \
  --resource-group rg-Hoang-LuanVan \
  -n agenticsdlc-dev
```

## 5. Monitor

- **Logs**: `az containerapp logs show -n agenticsdlc-dev -g rg-Hoang-LuanVan --tail 100`
- **Application Insights**: <https://portal.azure.com> → Application Insights → `agenticsdlc-ai-dev`
- **Live metrics**: Application Insights → Live Metrics
- **Cost log**: query KQL in App Insights:
  ```kusto
  traces
  | where message contains "RequirementAgent done" or message contains "CodingAgent done"
  | extend cost = todouble(extract("\\$([0-9.]+)", 1, message))
  | summarize total_cost = sum(cost), n = count() by bin(timestamp, 1h)
  ```

## 6. Cleanup

```bash
az group delete --name rg-Hoang-LuanVan --yes --no-wait
# Key Vault soft-delete → purge after 7 days or proactively:
# az keyvault purge --name <kv-name>
```

## Troubleshooting

| Symptom | Common cause | Fix |
|---|---|---|
| Workflow fails at `az containerapp update` | ACR not set up | Run step 1.3 manually the first time |
| Container app returns 503 after deploy | Probe `/health` timeout | Check the logs: `az containerapp logs show ...` |
| LLM call returns 401 from the Container App | Key Vault secret not set | Set via `az keyvault secret set ...` + restart the revision |
| App Insights cost spikes | Verbose log level | Reduce `Logging:LogLevel:Default` to `Warning` |
