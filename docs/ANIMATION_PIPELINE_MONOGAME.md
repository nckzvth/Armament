# MonoGame Animation Pipeline

## Goal

Use data-driven atlas animation playback in `client-mg` without changing server/shared-sim contracts.

## Asset Root

Use this root for runtime animation assets:

- `/Users/nckzvth/Projects/Armament/content/animations`

Per class layout:

- `/Users/nckzvth/Projects/Armament/content/animations/<class-id>/**/<clip>_atlas.json`
- `/Users/nckzvth/Projects/Armament/content/animations/<class-id>/**/<clip>_atlas.png`
- `/Users/nckzvth/Projects/Armament/content/animations/clipmaps/<class-id>.json` (explicit mapping)

Example:

- `/Users/nckzvth/Projects/Armament/content/animations/bastion/...`
- `/Users/nckzvth/Projects/Armament/content/animations/clipmaps/bastion.json`

## Runtime Contract (MonoGame)

- No runtime sprite creation per frame.
- Load atlas texture once; draw with source rectangles.
- Direction selection uses 8-way facing from movement/aim.
- Locomotion state machine is data-driven and resolves:
  - run: forward/back/left/right + 4 diagonals
  - strafe: left/right + 4 diagonals
  - idle turn clips: left/right when facing changes while idle
- LMB can chain clips; RMB/skills map to discrete clips from cast slot codes.
- If class/spec clip is missing, fallback to debug rectangle and log once.

## Clipmap Contract (Scalable Setup)

`clipmaps/<class-id>.json` supports:

- `default`: class-level baseline mapping
- `specs`: optional per-spec overrides (`spec.<class>.<spec>`)
- fields:
  - `idleClipId`, `moveClipId`, `blockLoopClipId`, `heavyClipId`
  - locomotion:
    - `runForwardClipId`, `runBackwardClipId`, `runLeftClipId`, `runRightClipId`
    - `runForwardLeftClipId`, `runForwardRightClipId`, `runBackwardLeftClipId`, `runBackwardRightClipId`
    - `strafeLeftClipId`, `strafeRightClipId`
    - `strafeForwardLeftClipId`, `strafeForwardRightClipId`
    - `strafeBackwardLeftClipId`, `strafeBackwardRightClipId`
    - `turnLeftClipId`, `turnRightClipId`
  - `fastChainClipIds`
  - `castClipBySlotLabel` (`E/R/Q/T/1/2/3/4`)

Template:

- `/Users/nckzvth/Projects/Armament/content/animations/clipmaps/_template.class.json`

## Add A New Class (Minimal Setup)

1. Copy atlas `.json` + `.png` files to:
   - `/Users/nckzvth/Projects/Armament/content/animations/<class-id>/...`
2. Copy template:
   - `_template.class.json` -> `<class-id>.json`
3. Fill clip IDs using atlas json filename stem (`*_atlas.json` without extension).
4. Run verify:
   - `/Users/nckzvth/Projects/Armament/ops/scripts/verify.sh`

The atlas validator enforces:

- frame rectangles in bounds
- directions/frame counts
- clipmap references only existing clip IDs

## AtlasValidator CLI

Project:

- `/Users/nckzvth/Projects/Armament/ops/tools/AtlasValidator`

Example (your requested style):

```bash
dotnet run --project /Users/nckzvth/Projects/Armament/ops/tools/AtlasValidator -- --input-dir /Users/nckzvth/Desktop/Art/Characters --fail-on-error
```

Recommended repo run:

```bash
dotnet run --project /Users/nckzvth/Projects/Armament/ops/tools/AtlasValidator -- --input-dir /Users/nckzvth/Projects/Armament/content/animations --fail-on-error --report-out /Users/nckzvth/Projects/Armament/.artifacts/atlas-validation-report.txt --catalog-out /Users/nckzvth/Projects/Armament/.artifacts/atlas-catalog.json --overlay-dir /Users/nckzvth/Projects/Armament/.artifacts/atlas-overlays
```

Supported options:

- `--input-dir <dir>`
- `--clipmap-dir <dir>`
- `--pivot-mode auto|frame|cell`
- `--fail-on-error`
- `--report-out <file>`
- `--catalog-out <file>`
- `--overlay-dir <dir>`
- `--write-fixes` (writes `*.fixed.json` with clamped rect/pivot fixes)
- `--generate-clipmaps --generate-clipmaps-dir <dir>` (scaffold per-class mappings)
- `--mapping-patch-in <file>` (apply mapping edits to class clipmaps)

Mapping patch file format:

```json
{
  "classId": "bastion",
  "updates": [
    { "scope": "default", "field": "moveClipId", "value": "bastion_basic_shield_HumanM@Run01_Forward_atlas" },
    { "scope": "spec.bastion.bulwark", "field": "cast:E", "value": "bastion_basic_shield_HumanM@BlockShield01 - Loop_atlas" }
  ]
}
```

Patch run:

```bash
dotnet run --project /Users/nckzvth/Projects/Armament/ops/tools/AtlasValidator -- --input-dir /Users/nckzvth/Projects/Armament/content/animations --clipmap-dir /Users/nckzvth/Projects/Armament/content/animations/clipmaps --mapping-patch-in /Users/nckzvth/Projects/Armament/.artifacts/bastion-patch.json --fail-on-error
```

## Current Status

- Bastion mappings are explicit in:
  - `/Users/nckzvth/Projects/Armament/content/animations/clipmaps/bastion.json`
- Validation is enforced in:
  - `/Users/nckzvth/Projects/Armament/ops/tools/AtlasValidator/Program.cs`
