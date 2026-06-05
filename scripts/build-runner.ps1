#!/usr/bin/env pwsh
# Publish AgentOs.RemoteAgent as a self-contained, single-file exe — one per target RID — into the
# Web's runner-dist/<rid>/ folder, so `GET /runner/download?rid=<rid>` can serve the right binary to
# the VS Code extension (and the Runners tab) without a .NET SDK or source checkout on the runner
# machine. ~60 MB each (no trimming — SignalR uses reflection). Cross-publishing from any OS is fine;
# the binaries are produced, not run, here.
#
# Run once after pulling, or whenever AgentOs.RemoteAgent changes. Output is gitignored. Pass -Rids to
# build a subset (e.g. -Rids win-x64 for a quick local build); defaults to all four desktop platforms.
param(
  [string[]] $Rids = @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src/AgentOs.RemoteAgent/AgentOs.RemoteAgent.csproj'
$root = Join-Path $repo 'src/AgentOs.Web/runner-dist'

foreach ($rid in $Rids) {
  $out = Join-Path $root $rid
  Write-Host "Publishing runner for $rid ..."
  dotnet publish $proj -c Release -r $rid --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out
  $exe = if ($rid -like 'win-*') { 'AgentOs.RemoteAgent.exe' } else { 'AgentOs.RemoteAgent' }
  Write-Host "  -> $out/$exe"
}

Write-Host "Runner published for: $($Rids -join ', ')"
