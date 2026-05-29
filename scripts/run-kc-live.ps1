# scripts/run-kc-live.ps1 — Windows companion to run-kc-live.sh.
# Usage:
#   $env:ANTHROPIC_API_KEY = "..."; $env:AZURE_OPENAI_API_KEY = "..."; $env:AZURE_OPENAI_ENDPOINT = "..."
#   .\scripts\run-kc-live.ps1 -Mode hybrid -N 10 -MaxUsd 5.00

param(
    [ValidateSet('hybrid','claude','azure')]
    [string]$Mode = 'hybrid',
    [int]$N = 10,
    [decimal]$MaxUsd = 5.00
)

if (-not $env:ANTHROPIC_API_KEY -and $Mode -ne 'azure') {
    Write-Error "ANTHROPIC_API_KEY not set; mode=$Mode needs Claude."
    exit 1
}
if (-not $env:AZURE_OPENAI_API_KEY -and $Mode -ne 'claude') {
    Write-Error "AZURE_OPENAI_API_KEY not set; mode=$Mode needs Azure."
    exit 1
}

$env:RUN_LIVE_LLM = '1'
$env:KC_LIVE_MODE = $Mode
$env:KC_LIVE_N = $N.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:KC_LIVE_MAX_USD = $MaxUsd.ToString([System.Globalization.CultureInfo]::InvariantCulture)

dotnet test tests/AgenticSdlc.Tests/AgenticSdlc.Tests.csproj `
    -c Release `
    --filter "FullyQualifiedName~KcLiveBenchTests"

$ts = (Get-Date -Format "yyyyMMddTHHmmssZ") -replace ':', ''
$dest = "docs/transcripts/kc_live/$Mode-$ts"
$src = "tests/AgenticSdlc.Tests/bin/Release/net10.0/TestResults"
New-Item -ItemType Directory -Force -Path $dest | Out-Null

if (Test-Path "$src/kc_live_summary.md") { Copy-Item "$src/kc_live_summary.md" "$dest/summary.md" }
if (Test-Path "$src/kc_live")            { Copy-Item "$src/kc_live/*" "$dest/" -Recurse }
if (Test-Path "$src/kc_metrics_live.csv"){ Copy-Item "$src/kc_metrics_live.csv" "$dest/metrics.csv" }

Write-Host "Transcripts copied to $dest"
