#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export DOTNET_CLI_HOME="$ROOT_DIR/.dotnet_home"

cd "$ROOT_DIR"
dotnet run --project client-mg/Armament.Client.MonoGame/Armament.Client.MonoGame.csproj
