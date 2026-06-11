#!/usr/bin/env bash
# postprovision.sh — run by `azd` after `azd provision` completes (Windows runs postprovision.ps1).
#
# Brings the imported 'agentic' realm in line with the cloud deployment:
#   1. Patches the 'agentic-web' client's redirectUris + webOrigins to the real ACA Web FQDN
#      (the realm import hardcodes https://localhost:5180) AND rotates the client secret to the
#      KEYCLOAKWEBCLIENTSECRET azd parameter (the import bakes the public dev secret — the Web app
#      already receives the azd value, so without this patch every login fails on secret mismatch).
#   2. Disables realm verifyEmail in cloud — no SMTP is wired yet (roadmap decision D4).
#   3. Rotates the imported seed users' passwords (operator/member) to OPERATORPASSWORD /
#      MEMBERPASSWORD when set — the import ships public placeholder passwords.
#
# Required env vars (set via `azd env set`):
#   KEYCLOAK_BASE_URL      — public FQDN of the KC container app
#   WEB_BASE_URL           — public FQDN of the Web container app
#   KEYCLOAKADMINPASSWORD  — KC admin password (azd parameter)
#   KEYCLOAKWEBCLIENTSECRET— agentic-web client secret (azd parameter)
# Optional:
#   OPERATORPASSWORD, MEMBERPASSWORD — new seed-user passwords (skipped when unset)
#
# Run manually:  bash infra/hooks/postprovision.sh
set -euo pipefail

KC_URL="${KEYCLOAK_BASE_URL:-}"
WEB_URL="${WEB_BASE_URL:-}"
KC_ADMIN_PASS="${KEYCLOAKADMINPASSWORD:-}"
KC_ADMIN_USER="${KEYCLOAKADMINUSERNAME:-admin}"
KC_WEB_SECRET="${KEYCLOAKWEBCLIENTSECRET:-}"
REALM="agentic"
CLIENT_ID="agentic-web"

if [[ -z "$KC_URL" || -z "$WEB_URL" || -z "$KC_ADMIN_PASS" ]]; then
  echo "Skipping KC realm patch — KEYCLOAK_BASE_URL, WEB_BASE_URL, or KEYCLOAKADMINPASSWORD not set."
  echo ""
  echo "After the first 'azd up', run:"
  echo "  azd env set KEYCLOAK_BASE_URL  'https://keycloak.<env>.azurecontainerapps.io'"
  echo "  azd env set WEB_BASE_URL       'https://web.<env>.azurecontainerapps.io'"
  echo "  azd provision   # re-runs this hook"
  exit 0
fi

echo "Patching realm '$REALM' for $WEB_URL"

# Admin token
TOKEN=$(curl -sf -X POST "$KC_URL/realms/master/protocol/openid-connect/token" \
  -d "grant_type=password" \
  -d "client_id=admin-cli" \
  -d "username=$KC_ADMIN_USER" \
  -d "password=$KC_ADMIN_PASS" | jq -r '.access_token')

if [[ -z "$TOKEN" || "$TOKEN" == "null" ]]; then
  echo "ERROR: Failed to get KC admin token. Is KC_URL correct and KC up?"
  exit 1
fi

auth=(-H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json")

# 1. agentic-web client: redirect URIs + origins + secret
CLIENT_UUID=$(curl -sf "${auth[@]}" \
  "$KC_URL/admin/realms/$REALM/clients?clientId=$CLIENT_ID" | jq -r '.[0].id')

if [[ -z "$CLIENT_UUID" || "$CLIENT_UUID" == "null" ]]; then
  echo "ERROR: Client '$CLIENT_ID' not found in realm '$REALM'."
  exit 1
fi

CLIENT_PATCH="{\"redirectUris\":[\"$WEB_URL/*\"],\"webOrigins\":[\"$WEB_URL\"]"
if [[ -n "$KC_WEB_SECRET" ]]; then
  CLIENT_PATCH+=",\"secret\":$(jq -Rn --arg s "$KC_WEB_SECRET" '$s')"
fi
CLIENT_PATCH+="}"

curl -sf -X PUT "${auth[@]}" "$KC_URL/admin/realms/$REALM/clients/$CLIENT_UUID" -d "$CLIENT_PATCH" \
  && echo "Client patched: redirectUris=$WEB_URL/* secret=$([[ -n "$KC_WEB_SECRET" ]] && echo rotated || echo unchanged)" \
  || { echo "ERROR: Failed to patch KC client."; exit 1; }

# 2. Realm: verifyEmail off in cloud (no SMTP yet — D4)
curl -sf -X PUT "${auth[@]}" "$KC_URL/admin/realms/$REALM" -d '{"realm":"agentic","verifyEmail":false}' \
  && echo "Realm verifyEmail=false" \
  || echo "WARN: Failed to set verifyEmail=false."

# 3. Seed users: rotate imported placeholder passwords
rotate_user() {
  local username="$1" newpass="$2"
  if [[ -z "$newpass" ]]; then
    echo "WARN: ${username^^}PASSWORD not set — seed user '$username' keeps the public imported password."
    return 0
  fi
  local uid
  uid=$(curl -sf "${auth[@]}" "$KC_URL/admin/realms/$REALM/users?username=$username&exact=true" | jq -r '.[0].id')
  if [[ -z "$uid" || "$uid" == "null" ]]; then
    echo "WARN: seed user '$username' not found — skipping."
    return 0
  fi
  curl -sf -X PUT "${auth[@]}" "$KC_URL/admin/realms/$REALM/users/$uid/reset-password" \
    -d "$(jq -n --arg p "$newpass" '{type:"password",value:$p,temporary:false}')" \
    && echo "Seed user '$username' password rotated." \
    || echo "WARN: Failed to rotate '$username' password."
}

rotate_user "operator" "${OPERATORPASSWORD:-}"
rotate_user "member"   "${MEMBERPASSWORD:-}"

echo "Realm patch complete."
