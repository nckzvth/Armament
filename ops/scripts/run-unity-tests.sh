#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT_PATH="$ROOT_DIR/client-unity"
RESULTS_DIR="$ROOT_DIR/.artifacts/unity-tests"
mkdir -p "$RESULTS_DIR"

UNITY_BIN="${UNITY_PATH:-}"
if [[ -z "$UNITY_BIN" ]]; then
  if compgen -G "/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity" > /dev/null; then
    UNITY_BIN="$(ls -1 /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity | sort -V | tail -n 1)"
  fi
fi

if [[ -z "$UNITY_BIN" || ! -x "$UNITY_BIN" ]]; then
  echo "Unity executable not found. Set UNITY_PATH to Unity 6 editor binary." >&2
  exit 1
fi

run_tests() {
  local platform="$1"
  local results_file="$RESULTS_DIR/${platform}-results.xml"
  local log_file="$RESULTS_DIR/${platform}.log"

  "$UNITY_BIN" \
    -batchmode \
    -nographics \
    -projectPath "$PROJECT_PATH" \
    -runTests \
    -testPlatform "$platform" \
    -testResults "$results_file" \
    -logFile "$log_file" \
    -quit
}

run_tests EditMode
run_tests PlayMode

echo "Unity batch tests completed. Results at $RESULTS_DIR"
