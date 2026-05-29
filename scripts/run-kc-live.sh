#!/usr/bin/env bash
# scripts/run-kc-live.sh — drive tests/AgenticSdlc.Tests/Smoke/KcLiveBenchTests.cs
# against a real LLM (hybrid by default). Cost-bounded; transcripts land in
# tests/AgenticSdlc.Tests/bin/Release/net10.0/TestResults/kc_live/.
#
# Usage:
#   ANTHROPIC_API_KEY=... AZURE_OPENAI_API_KEY=... AZURE_OPENAI_ENDPOINT=... \
#     scripts/run-kc-live.sh [hybrid|claude|azure] [n] [maxUsd]
#
# After the run finishes, the script copies summary.md + run-*.json into
# docs/transcripts/kc_live/<mode>-<timestamp>/ for the thesis defense.

set -euo pipefail

MODE="${1:-hybrid}"
N="${2:-10}"
MAX_USD="${3:-5.00}"

if [ -z "${ANTHROPIC_API_KEY:-}" ] && [ "$MODE" != "azure" ]; then
  echo "ANTHROPIC_API_KEY not set; mode=$MODE needs Claude." >&2
  exit 1
fi
if [ -z "${AZURE_OPENAI_API_KEY:-}" ] && [ "$MODE" != "claude" ]; then
  echo "AZURE_OPENAI_API_KEY not set; mode=$MODE needs Azure." >&2
  exit 1
fi

export RUN_LIVE_LLM=1
export KC_LIVE_MODE="$MODE"
export KC_LIVE_N="$N"
export KC_LIVE_MAX_USD="$MAX_USD"

dotnet test tests/AgenticSdlc.Tests/AgenticSdlc.Tests.csproj \
  -c Release \
  --filter "FullyQualifiedName~KcLiveBenchTests"

TS="$(date -u +%Y%m%dT%H%M%SZ)"
DEST="docs/transcripts/kc_live/${MODE}-${TS}"
SRC="tests/AgenticSdlc.Tests/bin/Release/net10.0/TestResults"
mkdir -p "$DEST"
if [ -f "$SRC/kc_live_summary.md" ]; then
  cp "$SRC/kc_live_summary.md" "$DEST/summary.md"
fi
if [ -d "$SRC/kc_live" ]; then
  cp -r "$SRC/kc_live/." "$DEST/"
fi
if [ -f "$SRC/kc_metrics_live.csv" ]; then
  cp "$SRC/kc_metrics_live.csv" "$DEST/metrics.csv"
fi
echo "Transcripts copied to $DEST"
