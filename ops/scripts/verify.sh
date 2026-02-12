#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export DOTNET_CLI_HOME="$ROOT_DIR/.dotnet_home"

cd "$ROOT_DIR"

if [ -d "$ROOT_DIR/client-unity" ]; then
  echo "ERROR: legacy Unity client directory exists at $ROOT_DIR/client-unity" >&2
  exit 1
fi

if [ -f "$ROOT_DIR/ops/scripts/run-unity-tests.sh" ] || [ -f "$ROOT_DIR/ops/scripts/run-unity-tests.ps1" ]; then
  echo "ERROR: legacy Unity runner scripts still exist in ops/scripts" >&2
  exit 1
fi

dotnet --info

dotnet format shared-sim/Armament.SharedSim.sln --verify-no-changes

dotnet format server-dotnet/Armament.Server.sln --verify-no-changes

dotnet run --project shared-sim/Tests/SharedSim.Tests/Armament.SharedSim.Tests.csproj
dotnet run --project shared-sim/Tests/ContentValidation.Tests/Armament.ContentValidation.Tests.csproj

dotnet run --project server-dotnet/Tests/GameServer.Tests/Armament.GameServer.Tests.csproj

dotnet run --project server-dotnet/Tests/Persistence.Tests/Armament.Persistence.Tests.csproj

dotnet build client-mg/Armament.Client.MonoGame/Armament.Client.MonoGame.csproj
