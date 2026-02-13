#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
INPUT_DIR="${1:-$ROOT_DIR/content/animations}"

cd "$ROOT_DIR"

dotnet run --project "$ROOT_DIR/ops/tools/AtlasEditor/Armament.AtlasEditor.csproj" -- --input-dir "$INPUT_DIR"
