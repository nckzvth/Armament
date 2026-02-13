# Armament (MonoGame Client + .NET Authoritative Server)

This repo now runs with a **MonoGame client** (`client-mg`) and a **.NET authoritative UDP server** (`server-dotnet`), sharing protocol/sim contracts from `shared-sim`.

## Repo Layout

- `/Users/nckzvth/Projects/Armament/client-mg` MonoGame DesktopGL client (C#)
- `/Users/nckzvth/Projects/Armament/server-dotnet` authoritative server host/game loop/transport
- `/Users/nckzvth/Projects/Armament/shared-sim` deterministic shared protocol/sim contracts (`netstandard2.1`)
- `/Users/nckzvth/Projects/Armament/ops/scripts` run + verify scripts
- `/Users/nckzvth/Projects/Armament/docs` architecture/protocol/persistence/determinism docs

## Prerequisites (macOS)

1. Install .NET SDK 9 (recommended for MonoGame 3.8.4.1).
2. Install MonoGame templates once:

```bash
dotnet new install MonoGame.Templates.CSharp
```

3. Optional but recommended for persistence tests: Docker Desktop running.

## Start Server

```bash
cd /Users/nckzvth/Projects/Armament
export ARMAMENT_DB_CONNECTION='Host=127.0.0.1;Port=5432;Database=armament_dev;Username=<your-macos-user>'
dotnet run --project server-dotnet/Src/ServerHost/Armament.ServerHost.csproj -- --port 9000 --simulation-hz 60 --snapshot-hz 10
```

If you do not want persistence writes while testing client behavior, omit `ARMAMENT_DB_CONNECTION`.

## Start MonoGame Client

```bash
cd /Users/nckzvth/Projects/Armament
./ops/scripts/run-client-mg.sh
```

Windows:

```powershell
./ops/scripts/run-client-mg.ps1
```

## Start Server + Client (single command)

```bash
cd /Users/nckzvth/Projects/Armament
./ops/scripts/run-dev.sh
```

## MonoGame Client Controls

### Gameplay

- `WASD`: move
- `LMB`: fast attack hold
- `RMB`: heavy attack hold
- `Shift`: block
- `E/R/Q/T/1/2/3/4`: skills
- `Z`: pickup intent
- `F`: portal interact
- `H`: return from dungeon to overworld
- `Option/Alt`: toggle loot names

### UI Flow (in window)

- Login screen on launch
- Character select with filled slots + next-empty create entry
- Character creation with class/spec selection
- In-game pause menu on `Esc`:
  - Resume
  - Return to Character Select
  - Logout
  - Settings
  - Exit

Config is persisted at:

- `~/Library/Application Support/Armament/client-mg/config.json`

## Atlas Validation

- Atlas clip JSON + PNG bounds are validated by:
  - `/Users/nckzvth/Projects/Armament/ops/tools/AtlasValidator`
- It runs automatically in verify and fails fast for invalid frame rectangles.
- Standalone run (source art or repo assets):
  - `dotnet run --project /Users/nckzvth/Projects/Armament/ops/tools/AtlasValidator -- --input-dir /Users/nckzvth/Desktop/Art/Characters --fail-on-error`
- Class animation mappings are explicit in:
  - `/Users/nckzvth/Projects/Armament/content/animations/clipmaps/<class-id>.json`
- Mapping template for new classes:
  - `/Users/nckzvth/Projects/Armament/content/animations/clipmaps/_template.class.json`

## Atlas Editor (GUI)

- Desktop editor tool:
  - `/Users/nckzvth/Projects/Armament/ops/tools/AtlasEditor`
- Launch:
  - `/Users/nckzvth/Projects/Armament/ops/scripts/run-atlas-editor.sh`
  - `/Users/nckzvth/Projects/Armament/ops/scripts/run-atlas-editor.ps1`
- Full usage:
  - `/Users/nckzvth/Projects/Armament/docs/ATLAS_EDITOR.md`

## Verified MonoGame Parity Slice

Current MonoGame parity proven against existing server contract:

- UDP connect/handshake/join flow
- fixed-tick input command stream with sequence/client tick
- local prediction + reconciliation using snapshot `LastProcessedInputSequence`
- interpolation for remote entities
- overworld/dungeon zone transitions
- action flags + server cast feedback feed
- debug rendering for players/enemies/loot/zones/links

## Verification (single entrypoint)

macOS/Linux:

```bash
./ops/scripts/verify.sh
```

Windows:

```powershell
./ops/scripts/verify.ps1
```

`verify` runs:

1. `dotnet --info`
2. `dotnet format --verify-no-changes` (shared-sim + server + client-mg)
3. shared-sim tests (determinism + content validation)
4. server tests (game server + persistence integration)
5. MonoGame client build
6. MonoGame headless logic tests (`client-mg/Tests/Armament.Client.MonoGame.Tests`)

## Manual Playtest Checklist

1. Start server on `:9000` and launch client.
2. Log in with username/password placeholder values.
3. Create/select character in slot flow.
4. Press `PLAY` and confirm HUD shows joined state and snapshots.
5. Move with `WASD`, observe local responsiveness and remote interpolation.
6. Use `F` near portal to enter dungeon.
7. Use `H` in dungeon to return overworld.
8. Trigger skills and confirm authoritative cast feed entries.
9. Click loot or use `Z`, confirm pickup.
10. Press `Alt` to toggle loot names.

## Notes

- Unity client has been removed from this repository. The active client is MonoGame only.
- The active client path is `client-mg`.
