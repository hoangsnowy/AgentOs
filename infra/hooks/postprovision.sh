#!/usr/bin/env bash
# postprovision.sh — run by `azd` after `azd provision` completes.
#
# Patches the 'agentic-web' Keycloak client's redirectUris + webOrigins to use the real
# Azure Container Apps FQDN for the Web app (instead of the hardcoded localhost:5180 from
# the realm import). Also prints the KC DB bootstrap instructions.
#
# Required env vars (set via `azd env set`):
#   KEYCLOAK_BASE_URL   — public FQDN of the KC container, e.g. https://keycloak.<env>.azurecontainerapps.io
#   WEB_BASE_URL        — public FQDN of the Web container, e.g. https://web.<env>.azurecontainerapps.io
#   KEYCLOAKADMINPASSWORD — KC admin password (same as azd env parameter)
#
# Run manually:  bash infra/hooks/postprovision.sh
set -euo pipefail

KC_URL="${KEYCLOAK_BASE_URL:-}"
WEB_URL="${WEB_BASE_URL:-}"
KC_ADMIN_PASS="${KEYCLOAKADMINPASSWORD:-}"
KC_ADMIN_USER="${KEYCLOAKADMINUSERNAME:-admin}"
REALM="agentic"
CLIENT_ID="agentic-web"

if [[ -z "$KC_URL" || -z "$WEB_URL" || -z "$KC_ADMIN_PASS" ]]; then
  echo "Skipping KC client patch — KEYCLOAK_BASE_URL, WEB_BASE_URL, or KEYCLOAKADMINPASSWORD not set."
  echo ""
  echo "To enable, run:"
  echo "  azd env set KEYCLOAK_BASE_URL  'https://keycloak.<env>.azurecontainerapps.io'"
  echo "  azd env set WEB_BASE_URL       'https://web.<env>.azurecontainerapps.io'"
  echo ""
  echo "KC Postgres bootstrap (run after KC is up):"
  echo "  1. Create 'keycloak' database on the provisioned Postgres server"
  echo "  2. azd env set KEYCLOAKDBURL      'jdbc:postgresql://<host>:5432/keycloak?sslmode=require'"
  echo "  3. azd env set KEYCLOAKDBUSERNAME '<admin-login>'"
  echo "  4. azd env set KEYCLOAKDBPASSWORD '<password>'"
  echo "  5. azd env set KEYCLOAKHOSTNAME   '$KC_URL'"
  echo "  6. azd up"
  exit 0
fi

echo "Patching KC client '$CLIENT_ID' redirectUris + webOrigins → $WEB_URL"

# Get admin token
TOKEN=$(curl -sf -X POST "$KC_URL/realms/master/protocol/openid-connect/token" \
  -d "grant_type=password" \
  -d "client_id=admin-cli" \
  -d "username=$KC_ADMIN_USER" \
  -d "password=$KC_ADMIN_PASS" | jq -r '.access_token')

if [[ -z "$TOKEN" || "$TOKEN" == "null" ]]; then
  echo "ERROR: Failed to get KC admin token. Is KC_URL correct and KC up?"
  exit 1
fi

# Find client UUID
CLIENT_UUID=$(curl -sf -H "Authorization: Bearer $TOKEN" \
  "$KC_URL/admin/realms/$REALM/clients?clientId=$CLIENT_ID" | jq -r '.[0].id')

if [[ -z "$CLIENT_UUID" || "$CLIENT_UUID" == "null" ]]; then
  echo "ERROR: Client '$CLIENT_ID' not found in realm '$REALM'."
  exit 1
fi

# Patch redirectUris + webOrigins
curl -sf -X PUT "$KC_URL/admin/realms/$REALM/clients/$CLIENT_UUID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"redirectUris\":[\"$WEB_URL/*\"],\"webOrigins\":[\"$WEB_URL\"]}" \
  && echo "KC client patched: redirectUris=$WEB_URL/*" \
  || echo "ERROR: Failed to patch KC client."
