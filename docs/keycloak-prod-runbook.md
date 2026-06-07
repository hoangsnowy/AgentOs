# Keycloak-on-Postgres production runbook (azd → Azure Container Apps)

This is the **azd-only** half of deploy blocker #1/#2/#6 — the parts that can only be validated by an
actual `azd up` against your subscription. Topology chosen: **persistent Keycloak backed by the same
Postgres flexible server** (keeps the `agentic` realm + AgentOS theme + the Tenants module's Keycloak
admin provisioning). Batch-3 code hardening (ForwardedHeaders, durable DataProtection, Web→Production,
`KC_PROXY=edge`) is already in `main`; this runbook layers the remaining infra on top.

> Legend: ✅ = stable command, run as-is · ⚠️ = needs a deploy iteration to confirm against the preview
> `Aspire.Hosting.Keycloak` 13.3.5 API (verify in the first `azd up` logs).

Prereqs: `azd`, `az`, .NET 10 SDK, an Azure subscription. `AZURE_ENV_NAME` + `AZURE_LOCATION` chosen.

---

## Step 0 — first provision (gets you the FQDNs you need for everything else)

The Web/Keycloak ingress FQDNs only exist *after* provisioning. Do one provisioning pass first, read
the names back, then configure.

```bash
azd auth login
azd up                      # pick env name + subscription + region; creates RG, ACR, ACA env, Postgres, etc.
azd env get-values          # note SERVICES_WEB_URL, SERVICES_KEYCLOAK_URL (or the containerapp FQDNs)
```

If `azd env get-values` doesn't surface the Keycloak URL, read it from Azure:

```bash
RG=$(azd env get-values | sed -n 's/^AZURE_RESOURCE_GROUP=//p' | tr -d '"')
az containerapp show -n keycloak -g "$RG" --query properties.configuration.ingress.fqdn -o tsv
az containerapp show -n web      -g "$RG" --query properties.configuration.ingress.fqdn -o tsv
```

Call them `KC_FQDN` (e.g. `keycloak.<hash>.<region>.azurecontainerapps.io`) and `WEB_FQDN`.

---

## Step 1 — rotate the seeded credentials (blocker #2) ✅

A default deploy ships `admin/admin`, the committed OIDC secret `agentic-web-dev-secret`, and seed realm
users `operator/operator` + `member/member`. Rotate the secrets via azd (stored in Key Vault, injected
as the Aspire parameters), then redeploy:

```bash
azd env set KeycloakAdminUsername    "agentos-admin"
azd env set KeycloakAdminPassword    "$(openssl rand -base64 24)"
azd env set KeycloakWebClientSecret  "$(openssl rand -base64 32)"
```

Keep the client secret value — Step 4 writes the *same* value into the realm's `agentic-web` client.
**Strip the seed users** from `infra/keycloak/agentic-realm.json` before they ship to a public env
(delete the `operator`/`member` entries under `"users"`), or keep them only behind a run-mode-gated
realm (see Step 3 note). Commit the stripped realm.

---

## Step 2 — give Keycloak its own database on the flexible server ✅

Don't share the `agentos` app DB. Create a `keycloak` database:

```bash
RG=$(azd env get-values | sed -n 's/^AZURE_RESOURCE_GROUP=//p' | tr -d '"')
PG=$(az postgres flexible-server list -g "$RG" --query "[0].name" -o tsv)
az postgres flexible-server db create -g "$RG" -s "$PG" -d keycloak
PG_HOST=$(az postgres flexible-server show -g "$RG" -n "$PG" --query fullyQualifiedDomainName -o tsv)
echo "KC_DB_URL=jdbc:postgresql://$PG_HOST:5432/keycloak?sslmode=require"
```

Capture the admin login of the flexible server (azd set it at provision; read from Key Vault or reset):

```bash
az postgres flexible-server update -g "$RG" -n "$PG" --admin-password "$(openssl rand -base64 24)"
```

---

## Step 3 — make the AppHost Keycloak persistent + themed (publish mode) ⚠️

Apply this to `infra/AgentOs.AppHost/Program.cs`. It is **publish-mode only** — the `if (!isPublish)`
branch (local H2 + bind-mount theme) is untouched, so `dotnet run` is unchanged. Add to the existing
`else` branch (which already sets `KC_PROXY=edge`):

```csharp
else
{
    var kcDbUrl      = builder.AddParameter("KeycloakDbUrl");                 // azd env set (Step 2)
    var kcDbUsername = builder.AddParameter("KeycloakDbUsername");
    var kcDbPassword = builder.AddParameter("KeycloakDbPassword", secret: true);
    var webOrigin    = builder.AddParameter("WebPublicOrigin");               // https://<WEB_FQDN>

    keycloak
        .WithEnvironment("KC_PROXY", "edge")
        .WithEnvironment("KC_PROXY_HEADERS", "xforwarded")
        .WithEnvironment("KC_HTTP_ENABLED", "true")
        .WithEnvironment("KC_HOSTNAME", kcHostname)        // https://<KC_FQDN> — set as a parameter too
        // Persist to Postgres instead of ephemeral H2:
        .WithEnvironment("KC_DB", "postgres")
        .WithEnvironment("KC_DB_URL", kcDbUrl)
        .WithEnvironment("KC_DB_USERNAME", kcDbUsername)
        .WithEnvironment("KC_DB_PASSWORD", kcDbPassword)
        // Realm import still runs; the realm's redirectUris read ${WEB_ORIGIN} (Step 4):
        .WithEnvironment("WEB_ORIGIN", webOrigin);
}
```

```bash
azd env set KeycloakDbUrl       "jdbc:postgresql://$PG_HOST:5432/keycloak?sslmode=require"
azd env set KeycloakDbUsername  "<pg-admin-user>"
azd env set KeycloakDbPassword  "<pg-admin-password>"
azd env set WebPublicOrigin     "https://$WEB_FQDN"
azd env set KeycloakHostname    "https://$KC_FQDN"
```

**Theme baking** (the bind-mount path doesn't exist in ACA): the Aspire Keycloak image won't carry the
`agentos` theme in publish. Easiest reliable route — bake a custom image and point the resource at it:

```dockerfile
# infra/keycloak/Dockerfile
FROM quay.io/keycloak/keycloak:26.1 AS build
ENV KC_DB=postgres
COPY themes/agentos /opt/keycloak/themes/agentos
RUN /opt/keycloak/bin/kc.sh build
FROM quay.io/keycloak/keycloak:26.1
COPY --from=build /opt/keycloak/ /opt/keycloak/
```

⚠️ Wiring `AddKeycloak` to a custom Dockerfile under the preview integration is the one step to confirm
interactively — if `.WithDockerfile(...)` isn't exposed on the Keycloak resource builder in 13.3.5, fall
back to: push the image to your ACR (`az acr build -r <acr> -t agentos-keycloak:1 infra/keycloak`) and
reference it, or ship the theme via an ACA volume mount. The login still works on the default theme — the
theme is cosmetic, not a blocker.

---

## Step 4 — point the realm at the real Web FQDN (blocker #6)

Two ways; pick one.

**(a) Parameterize the realm JSON** (keeps local working via the default). In
`infra/keycloak/agentic-realm.json`, change the `agentic-web` client:

```jsonc
"redirectUris": ["${WEB_ORIGIN:https://localhost:5180}/*"],
"webOrigins":   ["${WEB_ORIGIN:https://localhost:5180}"],
"secret":       "${KC_WEB_CLIENT_SECRET:agentic-web-dev-secret}",
```

Keycloak substitutes `${ENV:default}` at import. `WEB_ORIGIN` is injected in Step 3; also inject
`KC_WEB_CLIENT_SECRET` (= the `KeycloakWebClientSecret` value). ⚠️ Confirm substitution in the first
`azd up` Keycloak logs (`Imported realm agentic`) — env-substitution in realm import is supported but
version-sensitive.

**(b) Configure post-provision via the admin API** (no realm edit, fully deterministic):

```bash
KC_ADMIN_PW=$(azd env get-values | sed -n 's/^KeycloakAdminPassword=//p' | tr -d '"')
TOKEN=$(curl -s -X POST "https://$KC_FQDN/realms/master/protocol/openid-connect/token" \
  -d "grant_type=password&client_id=admin-cli&username=agentos-admin&password=$KC_ADMIN_PW" \
  | python3 -c 'import json,sys;print(json.load(sys.stdin)["access_token"])')
CID=$(curl -s "https://$KC_FQDN/admin/realms/agentic/clients?clientId=agentic-web" \
  -H "Authorization: Bearer $TOKEN" | python3 -c 'import json,sys;print(json.load(sys.stdin)[0]["id"])')
curl -s -X PUT "https://$KC_FQDN/admin/realms/agentic/clients/$CID" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d "{\"redirectUris\":[\"https://$WEB_FQDN/*\"],\"webOrigins\":[\"https://$WEB_FQDN\"]}"
```

---

## Step 5 — real SMTP (so signup verification + invites actually send)

MailHog is dev-only. The realm has `verifyEmail:true`, so without real SMTP, signup is blocked. Wire a
provider (Azure Communication Services Email, SendGrid, etc.) into both the app and the realm:

```bash
azd env set Email__SmtpHost  "<smtp-host>"
azd env set Email__SmtpPort  "587"
azd env set Email__SmtpUser  "<user>"
azd env set Email__SmtpPass  "<pass>"
azd env set Email__From      "noreply@<your-domain>"
```

Set the realm `smtpServer` block (host/port/from/auth) the same way — via the admin API (like Step 4b) or
in `agentic-realm.json`. Or, to defer email entirely for a first deploy, set the realm `verifyEmail:false`.

---

## Step 6 — redeploy + verify ✅

```bash
azd up
```

Then confirm the things only a real deploy proves:

1. **Login** — open `https://$WEB_FQDN`, sign in. No `Invalid redirect_uri`, no `Correlation failed`
   (ForwardedHeaders + durable DataProtection now handle these).
2. **Persistence across restart** — `az containerapp revision restart -n keycloak -g $RG`; log in again →
   the realm/users survive (proves Postgres backing, not H2).
3. **Saved secrets survive** — in the AgentOS Settings app save an LLM key, restart the `web` app, confirm
   it still decrypts (proves the durable DataProtection key ring).
4. **Bearer** — `curl https://<API_FQDN>/health` then an authenticated `/pipeline` call returns 200
   (proves `KC_HOSTNAME`/issuer alignment).

If any step fails, `az containerapp logs show -n keycloak -g $RG --tail 200` is the first place to look.

---

## What's intentionally NOT automated here

- The `.WithDockerfile` theme-image wiring (Step 3) and realm env-substitution (Step 4a) are the two
  preview-API-sensitive bits — confirm them in your first `azd up`, fall back to the admin-API / ACR-build
  routes if the fluent call isn't there.
- This runbook assumes the chosen topology (persistent Keycloak). If you later move to a managed IdP
  (Entra External ID), Steps 1–4 are replaced by an app-registration + a rewrite of the Tenants module's
  Keycloak admin REST calls to Microsoft Graph.
