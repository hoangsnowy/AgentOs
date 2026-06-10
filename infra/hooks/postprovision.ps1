# postprovision.ps1 — run by `azd` after `azd provision` completes (Windows; POSIX runs postprovision.sh).
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
# Required env vars (set via `azd env set`): KEYCLOAK_BASE_URL, WEB_BASE_URL, KEYCLOAKADMINPASSWORD,
# KEYCLOAKWEBCLIENTSECRET. Optional: OPERATORPASSWORD, MEMBERPASSWORD.
#
# Run manually:  pwsh infra/hooks/postprovision.ps1
$ErrorActionPreference = 'Stop'

$kcUrl       = $env:KEYCLOAK_BASE_URL
$webUrl      = $env:WEB_BASE_URL
$adminPass   = $env:KEYCLOAKADMINPASSWORD
$adminUser   = if ($env:KEYCLOAKADMINUSERNAME) { $env:KEYCLOAKADMINUSERNAME } else { 'admin' }
$webSecret   = $env:KEYCLOAKWEBCLIENTSECRET
$realm       = 'agentic'
$clientId    = 'agentic-web'

if (-not $kcUrl -or -not $webUrl -or -not $adminPass) {
    Write-Host "Skipping KC realm patch — KEYCLOAK_BASE_URL, WEB_BASE_URL, or KEYCLOAKADMINPASSWORD not set."
    Write-Host ""
    Write-Host "After the first 'azd up', run:"
    Write-Host "  azd env set KEYCLOAK_BASE_URL  'https://keycloak.<env>.azurecontainerapps.io'"
    Write-Host "  azd env set WEB_BASE_URL       'https://web.<env>.azurecontainerapps.io'"
    Write-Host "  azd provision   # re-runs this hook"
    exit 0
}

Write-Host "Patching realm '$realm' for $webUrl"

# Admin token
$tokenResp = Invoke-RestMethod -Method Post -Uri "$kcUrl/realms/master/protocol/openid-connect/token" `
    -ContentType 'application/x-www-form-urlencoded' `
    -Body @{ grant_type = 'password'; client_id = 'admin-cli'; username = $adminUser; password = $adminPass }
$token = $tokenResp.access_token
if (-not $token) { Write-Error "Failed to get KC admin token. Is KC_URL correct and KC up?"; exit 1 }

$headers = @{ Authorization = "Bearer $token" }

# 1. agentic-web client: redirect URIs + origins + secret
$clients = Invoke-RestMethod -Headers $headers -Uri "$kcUrl/admin/realms/$realm/clients?clientId=$clientId"
$clientUuid = $clients[0].id
if (-not $clientUuid) { Write-Error "Client '$clientId' not found in realm '$realm'."; exit 1 }

$clientPatch = @{ redirectUris = @("$webUrl/*"); webOrigins = @($webUrl) }
if ($webSecret) { $clientPatch.secret = $webSecret }
Invoke-RestMethod -Method Put -Headers $headers -Uri "$kcUrl/admin/realms/$realm/clients/$clientUuid" `
    -ContentType 'application/json' -Body ($clientPatch | ConvertTo-Json) | Out-Null
Write-Host "Client patched: redirectUris=$webUrl/* secret=$(if ($webSecret) { 'rotated' } else { 'unchanged' })"

# 2. Realm: verifyEmail off in cloud (no SMTP yet — D4)
try {
    Invoke-RestMethod -Method Put -Headers $headers -Uri "$kcUrl/admin/realms/$realm" `
        -ContentType 'application/json' -Body (@{ realm = $realm; verifyEmail = $false } | ConvertTo-Json) | Out-Null
    Write-Host "Realm verifyEmail=false"
} catch { Write-Warning "Failed to set verifyEmail=false: $_" }

# 3. Seed users: rotate imported placeholder passwords
function Rotate-SeedUser([string]$username, [string]$newPass) {
    if (-not $newPass) {
        Write-Warning "$($username.ToUpper())PASSWORD not set — seed user '$username' keeps the public imported password."
        return
    }
    $users = Invoke-RestMethod -Headers $headers -Uri "$kcUrl/admin/realms/$realm/users?username=$username&exact=true"
    if (-not $users -or -not $users[0].id) { Write-Warning "Seed user '$username' not found — skipping."; return }
    try {
        Invoke-RestMethod -Method Put -Headers $headers -Uri "$kcUrl/admin/realms/$realm/users/$($users[0].id)/reset-password" `
            -ContentType 'application/json' -Body (@{ type = 'password'; value = $newPass; temporary = $false } | ConvertTo-Json) | Out-Null
        Write-Host "Seed user '$username' password rotated."
    } catch { Write-Warning "Failed to rotate '$username' password: $_" }
}

Rotate-SeedUser 'operator' $env:OPERATORPASSWORD
Rotate-SeedUser 'member'   $env:MEMBERPASSWORD

Write-Host "Realm patch complete."
