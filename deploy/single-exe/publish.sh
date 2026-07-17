#!/usr/bin/env bash
# Build the Vue SPA and publish Factarium as a self-contained single-file executable.
# Usage: deploy/single-exe/publish.sh [runtime] [configuration] [output]
#   e.g. deploy/single-exe/publish.sh linux-x64
set -euo pipefail

RUNTIME="${1:-linux-x64}"
CONFIGURATION="${2:-Release}"
OUTPUT="${3:-artifacts/single-exe}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

echo "==> Building SPA"
(cd web && npm ci && npm run build)

echo "==> Publishing single-file self-contained exe ($RUNTIME)"
dotnet publish src/Factarium.Api/Factarium.Api.csproj \
  -c "$CONFIGURATION" -r "$RUNTIME" --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeAllContentForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -p:DebugSymbols=false \
  -o "$OUTPUT"

echo "==> Done -> $OUTPUT"
