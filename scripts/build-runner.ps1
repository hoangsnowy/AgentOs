#!/usr/bin/env pwsh
# Publish AgentOs.RemoteAgent as a self-contained, single-file win-x64 exe into the Web's runner-dist/
# folder, so `GET /runner/download` can serve it to the VS Code extension (and the Runners tab) without
# a .NET SDK or a source checkout on the runner machine. ~60 MB (no trimming — SignalR uses reflection).
#
# Run once after pulling, or whenever AgentOs.RemoteAgent changes. Output is gitignored.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src/AgentOs.RemoteAgent/AgentOs.RemoteAgent.csproj'
$out  = Join-Path $repo 'src/AgentOs.Web/runner-dist'

dotnet publish $proj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $out

Write-Host "Runner published -> $out/AgentOs.RemoteAgent.exe"
