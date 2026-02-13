#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ANIM_ROOT="$ROOT_DIR/content/animations"
ARTIFACT_ROOT="$ROOT_DIR/.artifacts"

TYPE="class"
TARGET_ID=""
RUN_EDIT=1
RUN_VALIDATE=1
FAIL_ON_ERROR=1

usage() {
  cat <<EOF
Usage:
  $(basename "$0") [--type class|enemy|npc|prop|all] [--id <id>] [--edit-only|--validate-only] [--no-fail]

Examples:
  $(basename "$0") --type class --id bastion
  $(basename "$0") --type enemy --id skeleton_mage
  $(basename "$0") --type all --validate-only

Behavior:
  - Default runs both Atlas Editor then AtlasValidator.
  - Validation outputs are written under .artifacts with a scoped suffix.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --type)
      TYPE="${2:-}"
      shift 2
      ;;
    --id)
      TARGET_ID="${2:-}"
      shift 2
      ;;
    --edit-only)
      RUN_EDIT=1
      RUN_VALIDATE=0
      shift
      ;;
    --validate-only)
      RUN_EDIT=0
      RUN_VALIDATE=1
      shift
      ;;
    --no-fail)
      FAIL_ON_ERROR=0
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown arg: $1" >&2
      usage
      exit 1
      ;;
  esac
done

TYPE="$(echo "$TYPE" | tr '[:upper:]' '[:lower:]')"
case "$TYPE" in
  class|enemy|npc|prop|all) ;;
  *)
    echo "Invalid --type '$TYPE'. Expected class|enemy|npc|prop|all." >&2
    exit 1
    ;;
esac

if [[ "$TYPE" != "all" && -z "$TARGET_ID" ]]; then
  echo "--id is required unless --type all is used." >&2
  exit 1
fi

resolve_scope_dir() {
  local type="$1"
  local id="$2"
  if [[ "$type" == "all" ]]; then
    echo "$ANIM_ROOT"
    return
  fi

  local candidates=()
  case "$type" in
    class)
      candidates+=("$ANIM_ROOT/$id" "$ANIM_ROOT/classes/$id" "$ANIM_ROOT/characters/$id")
      ;;
    enemy)
      candidates+=("$ANIM_ROOT/enemies/$id")
      ;;
    npc)
      candidates+=("$ANIM_ROOT/npcs/$id")
      ;;
    prop)
      candidates+=("$ANIM_ROOT/props/$id")
      ;;
  esac

  local c
  for c in "${candidates[@]}"; do
    if [[ -d "$c" ]]; then
      echo "$c"
      return
    fi
  done

  # Default create location when target folder is new.
  if [[ "$type" == "class" ]]; then
    echo "$ANIM_ROOT/$id"
  else
    echo "$ANIM_ROOT/${type}s/$id"
  fi
}

SCOPE_DIR="$(resolve_scope_dir "$TYPE" "$TARGET_ID")"
mkdir -p "$SCOPE_DIR"
mkdir -p "$ARTIFACT_ROOT"

SCOPE_SLUG="$TYPE"
if [[ -n "$TARGET_ID" ]]; then
  SCOPE_SLUG="${TYPE}.${TARGET_ID}"
fi

REPORT_OUT="$ARTIFACT_ROOT/atlas-validation-report.${SCOPE_SLUG}.txt"
CATALOG_OUT="$ARTIFACT_ROOT/atlas-catalog.${SCOPE_SLUG}.json"
OVERLAY_DIR="$ARTIFACT_ROOT/atlas-overlays/${SCOPE_SLUG}"
mkdir -p "$OVERLAY_DIR"

echo "[import-animation] root: $ROOT_DIR"
echo "[import-animation] scope: $SCOPE_DIR"

cd "$ROOT_DIR"

if [[ $RUN_EDIT -eq 1 ]]; then
  echo "[import-animation] opening Atlas Editor..."
  dotnet run --project "$ROOT_DIR/ops/tools/AtlasEditor/Armament.AtlasEditor.csproj" -- --input-dir "$SCOPE_DIR"
fi

if [[ $RUN_VALIDATE -eq 1 ]]; then
  echo "[import-animation] running AtlasValidator..."
  VALIDATE_ARGS=(
    --input-dir "$SCOPE_DIR"
    --report-out "$REPORT_OUT"
    --catalog-out "$CATALOG_OUT"
    --overlay-dir "$OVERLAY_DIR"
  )
  if [[ -d "$ANIM_ROOT/clipmaps" ]]; then
    VALIDATE_ARGS+=(--clipmap-dir "$ANIM_ROOT/clipmaps")
  fi
  if [[ $FAIL_ON_ERROR -eq 1 ]]; then
    VALIDATE_ARGS+=(--fail-on-error)
  fi

  dotnet run --project "$ROOT_DIR/ops/tools/AtlasValidator" -- "${VALIDATE_ARGS[@]}"
  echo "[import-animation] report: $REPORT_OUT"
  echo "[import-animation] catalog: $CATALOG_OUT"
  echo "[import-animation] overlays: $OVERLAY_DIR"
fi

echo "[import-animation] done."
