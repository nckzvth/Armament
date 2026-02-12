#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export DOTNET_CLI_HOME="$ROOT_DIR/.dotnet_home"

PORT="${ARMAMENT_PORT:-9000}"
SIM_HZ="${ARMAMENT_SIM_HZ:-60}"
SNAP_HZ="${ARMAMENT_SNAPSHOT_HZ:-10}"

cd "$ROOT_DIR"
mkdir -p "$ROOT_DIR/.artifacts"

echo "[dev] Starting server on UDP :$PORT ..."
dotnet run --project "$ROOT_DIR/server-dotnet/Src/ServerHost/Armament.ServerHost.csproj" -- --port "$PORT" --simulation-hz "$SIM_HZ" --snapshot-hz "$SNAP_HZ" > "$ROOT_DIR/.artifacts/dev-server.log" 2>&1 &
SERVER_PID=$!

cleanup() {
  if kill -0 "$SERVER_PID" >/dev/null 2>&1; then
    kill "$SERVER_PID" >/dev/null 2>&1 || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

# Wait up to 15s for server bind log.
for _ in $(seq 1 75); do
  if grep -q "UDP listening" "$ROOT_DIR/.artifacts/dev-server.log" 2>/dev/null; then
    echo "[dev] Server ready."
    break
  fi
  sleep 0.2
done

if ! grep -q "UDP listening" "$ROOT_DIR/.artifacts/dev-server.log" 2>/dev/null; then
  echo "[dev] Server failed to start. Log: $ROOT_DIR/.artifacts/dev-server.log"
  tail -n 80 "$ROOT_DIR/.artifacts/dev-server.log" || true
  exit 1
fi

echo "[dev] Launching MonoGame client ..."
dotnet run --project "$ROOT_DIR/client-mg/Armament.Client.MonoGame/Armament.Client.MonoGame.csproj"
