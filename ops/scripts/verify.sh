#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export DOTNET_CLI_HOME="$ROOT_DIR/.dotnet_home"

cd "$ROOT_DIR"

dotnet --info

dotnet format shared-sim/Armament.SharedSim.sln --verify-no-changes

dotnet format server-dotnet/Armament.Server.sln --verify-no-changes

dotnet run --project shared-sim/Tests/SharedSim.Tests/Armament.SharedSim.Tests.csproj

dotnet run --project server-dotnet/Tests/GameServer.Tests/Armament.GameServer.Tests.csproj

dotnet run --project server-dotnet/Tests/Persistence.Tests/Armament.Persistence.Tests.csproj

"$ROOT_DIR/ops/scripts/run-unity-tests.sh"
