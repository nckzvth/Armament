# Atlas Editor (GUI)

Standalone animation editor tool for manual updates + validation.

Project:

- `/Users/nckzvth/Projects/Armament/ops/tools/AtlasEditor`

## Launch

macOS/Linux:

```bash
cd /Users/nckzvth/Projects/Armament
./ops/scripts/run-atlas-editor.sh /Users/nckzvth/Projects/Armament/content/animations
```

Windows:

```powershell
cd /Users/nckzvth/Projects/Armament
./ops/scripts/run-atlas-editor.ps1 /Users/nckzvth/Projects/Armament/content/animations
```

Or direct dotnet run:

```bash
dotnet run --project /Users/nckzvth/Projects/Armament/ops/tools/AtlasEditor/Armament.AtlasEditor.csproj -- --input-dir /Users/nckzvth/Projects/Armament/content/animations
```

## One-Command Import Flow

Use this wrapper to run scoped edit + validation in one command:

macOS/Linux:

```bash
cd /Users/nckzvth/Projects/Armament
./ops/scripts/import-animation-assets.sh --type class --id bastion
```

Examples:

```bash
./ops/scripts/import-animation-assets.sh --type enemy --id skeleton_mage
./ops/scripts/import-animation-assets.sh --type npc --id town_guard
./ops/scripts/import-animation-assets.sh --type all --validate-only
```

Windows:

```powershell
cd /Users/nckzvth/Projects/Armament
./ops/scripts/import-animation-assets.ps1 --type class --id bastion
```

## What It Does

- Organized tree: `characters/enemies/npcs/props -> class -> clip`.
- Atlas preview with rectangle + pivot overlay.
- Zoom controls (`-`, slider, `+`, `Zoom To Frame`) for precise remapping.
- Manual frame editing:
  - direction, frame index, time
  - x, y, w, h
  - pivotX, pivotY
- Save edited clip JSON.
- Auto-remap helpers:
  - `Normalize Clip (Recommended)` (remap + pivot + reference propagation in one pass)
  - `Auto-Remap Frame`
  - `Auto-Remap Clip`
  - `Normalize Class` (batch normalize all clips for selected class)
- Class mapping editor:
  - idle/move/block/heavy
  - run/strafe directional hooks (forward/back/left/right + diagonals)
  - turn-left / turn-right hooks
  - fast chain
  - cast slots `E/R/Q/T/1/2/3/4`
- Save class clipmap JSON.
- Clip ID panel:
  - full ID + concise ID
  - copy full/copy concise
  - class-wide concise ID apply with clipmap reference rewrite
- Run validator from UI (`Validate` button) and show output paths.

## Validator Integration

Editor `Validate` executes AtlasValidator with:

- `--input-dir`
- `--fail-on-error`
- `--report-out /Users/nckzvth/Projects/Armament/.artifacts/atlas-validation-report.txt`
- `--catalog-out /Users/nckzvth/Projects/Armament/.artifacts/atlas-catalog.json`
- `--overlay-dir /Users/nckzvth/Projects/Armament/.artifacts/atlas-overlays`

## Notes

- This tool edits JSON directly; it does not modify PNG files.
- Use AtlasValidator `--write-fixes` for automatic clamped `*.fixed.json` generation when needed.
- Recommended defaults for broad atlas imports:
  - `Alpha Threshold = 80`
  - `Search Padding = 4`
  - `Safety Pad = 2`
  - `Border Expand = 12`
  - `Pivot Mode = Foot Contact`
  - `Lock X median` + `Per-direction X lock` enabled
  - `Lock X from reference direction` enabled
  - `Temporal smooth Y` enabled (`Temporal smooth X` optional)
  - `Reference Dir = SE (2)` with `Propagate Y` enabled
