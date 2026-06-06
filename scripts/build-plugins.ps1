#requires -Version 7
# Builds the sample plugin and drops its single assembly into each host's plugins/ folder, where the
# runtime plugin loader (AgentOs.SharedKernel.Plugins.PluginLoader) discovers it at startup. The output
# folders are gitignored. Run before launching a host if you want the sample plugin loaded.
#
#   pwsh scripts/build-plugins.ps1 [-Configuration Release]

param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'samples/AgentOs.Plugins.Sample/AgentOs.Plugins.Sample.csproj'

Write-Host "Building sample plugin ($Configuration)..."
dotnet build $proj -c $Configuration --nologo | Out-Host

$dll = Join-Path $root "samples/AgentOs.Plugins.Sample/bin/$Configuration/net10.0/AgentOs.Plugins.Sample.dll"
if (-not (Test-Path $dll)) { throw "Plugin DLL not found: $dll" }

foreach ($hostDir in @('src/AgentOs.Api/plugins', 'src/AgentOs.Web/plugins')) {
    $dest = Join-Path $root $hostDir
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item $dll $dest -Force
    Write-Host "Copied $(Split-Path $dll -Leaf) -> $hostDir"
}

Write-Host "Done. Restart the host to load the plugin."
