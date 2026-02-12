# Armament Foundation (Phase 4 Slice)

Authoritative UDP Unity/.NET ARPG foundation with shared deterministic sim, prediction/reconciliation, interpolation, combat controls, one enemy archetype, loot drops, and pickup.

## Repo Layout

- `/Users/nckzvth/Projects/Armament/client-unity` Unity 6 project
- `/Users/nckzvth/Projects/Armament/shared-sim` netstandard2.1 shared protocol/deterministic sim
- `/Users/nckzvth/Projects/Armament/server-dotnet` authoritative server host/game loop/transport
- `/Users/nckzvth/Projects/Armament/ops/scripts` verification and Unity batch runners
- `/Users/nckzvth/Projects/Armament/docs` architecture/protocol/determinism/persistence notes
- `/Users/nckzvth/Projects/Armament/docs/classes` class-system contract, capability matrix, templates, and staged implementation plan

## Prerequisites

- Unity 6 editor (`6000.x`)
- .NET SDK
  - Spec baseline: `.NET 8`
  - Current local workspace runs server/test projects on `.NET 9` (shared-sim remains `netstandard2.1`)
- Optional: `UNITY_PATH` if Unity is not in default Hub install path

## Run Server

```bash
dotnet run --project /Users/nckzvth/Projects/Armament/server-dotnet/Src/ServerHost/Armament.ServerHost.csproj -- --port 9000 --simulation-hz 60 --snapshot-hz 10
```

Enable persistence queue (recommended while testing progression):

```bash
export ARMAMENT_DB_CONNECTION='Host=127.0.0.1;Port=5432;Database=armament_dev;Username=postgres;Password=postgres'
dotnet run --project /Users/nckzvth/Projects/Armament/server-dotnet/Src/ServerHost/Armament.ServerHost.csproj -- --port 9000 --simulation-hz 60 --snapshot-hz 10
```

Expected startup output includes:
- `[Server] UDP listening on 0.0.0.0:9000`
- `[Server] Persistence queue enabled.` (when `ARMAMENT_DB_CONNECTION` is set)

Optional strict spec gate (recommended before validating a new kit):

```bash
export ARMAMENT_REQUIRED_SPEC_ID='spec.exorcist.inquisitor'
dotnet run --project /Users/nckzvth/Projects/Armament/server-dotnet/Src/ServerHost/Armament.ServerHost.csproj -- --port 9000 --simulation-hz 60 --snapshot-hz 10
```

Behavior:
- server starts only if the required spec is present in loaded ability profiles
- server fails fast with an explicit error if the spec is missing/uncompiled

## Play In Unity (Manual Evidence)

1. Open `/Users/nckzvth/Projects/Armament/client-unity` in Unity 6.
2. Press Play.
3. Verify compact debug HUD appears as a top strip plus bottom-left combat ticker.
4. Verify HUD shows `Account` and `Slot` for the current character identity.
4. Verify world entities: local player is green, enemy is red, loot drops are yellow.
5. Verify controls/outcomes: `WASD` move, hold `LMB` builds builder, hold `RMB` spends spender, hold `Shift` mitigates enemy damage, `E/R/Q/T/1/2/3/4` trigger skills, `Z` sends manual loot pickup intent, gold auto-loots immediately in range, `Option`/`Alt` toggles loot names, `F` interacts with portal/NPC, `H` returns to overworld.

If you cannot damage the enemy immediately, walk into melee range first.

## Local Verification (Single Entrypoint)

- macOS/Linux:
```bash
./ops/scripts/verify.sh
```

- Windows:
```powershell
./ops/scripts/verify.ps1
```

`verify` runs in order:
1. `dotnet --info`
2. `dotnet format --verify-no-changes` for shared/server solutions
3. shared-sim deterministic/combat tests
4. class content validation (schema/reference/slot coverage checks for `content/*`)
5. server smoke test (2 clients join, snapshots exchanged, enemy archetype present, combat loop affects state)
6. persistence integration test:
   - applies EF Core migrations
   - validates create/load character
   - validates transactional loot pickup with duplicate prevention
7. Unity batch tests (EditMode + PlayMode)

Persistence integration test uses Testcontainers (`postgres:16-alpine`) when Docker is available.  
If Docker is unavailable, set `ARMAMENT_TEST_DB_CONNECTION` to an external PostgreSQL test database.  
By default this test now fails (not skip) when no DB backend is available.  
Use `ARMAMENT_ALLOW_PERSISTENCE_SKIP=1` only for temporary local bypass.

## Unity Batch Tests Directly

- macOS/Linux:
```bash
./ops/scripts/run-unity-tests.sh
```

- Windows:
```powershell
./ops/scripts/run-unity-tests.ps1
```

## Stronger Playability Check (Recommended)

Run this sequence and verify each stage:
1. Terminal A: start server (`dotnet run ...ServerHost...`).
2. Unity editor play mode: use `LOGIN` screen (`ARMAMENT` title), log in with a username.
3. `CHARACTER SELECT`: choose slot, inspect slot summary, press `CREATE NEW` if empty.
4. `CHARACTER CREATION`: pick base class/spec, set name, press `CREATE`, then `PLAY`.
5. Move and attack enemy until yellow loot appears.
6. Move into pickup range of the purple `Test Relic` and press `Z`; confirm it despawns (manual pickup path).
7. Walk into yellow gold drop radius and confirm `Currency` increments automatically.
8. Press `Option`/`Alt` twice and confirm drop names toggle on then off.
9. After loot is claimed/auto-looted, confirm the yellow drop visual despawns immediately (no enlarged ghost square remains).
10. Move near portal and confirm `[F] Dungeon` prompt appears above it, then press `F` to enter dungeon and confirm HUD zone switches to `Dungeon` with a non-zero instance id.
11. Defeat dungeon enemies and confirm higher reward drop behavior.
12. Press `H` and confirm return to overworld.
13. Press `Esc` to open in-game menu and verify:
    - `RETURN TO CHARACTER SELECT` disconnects and returns to select flow
    - `LOGOUT` returns to login flow
    - `EXIT GAME` exits play session/application
14. Hold `Shift` while enemy attacks and confirm HP drops slower than without block.
15. Stop and restart server with the same `ARMAMENT_DB_CONNECTION`, rejoin with same character slot, and confirm previously earned currency persists.
16. Run `./ops/scripts/verify.sh` and ensure all checks pass.
17. Create/select a `dreadweaver / spec.dreadweaver.menace` character:
    - Press `R` near an enemy to create tether (`Tether owner->target` label appears).
    - Keep enemy in range and confirm periodic HP loss/pull while tether is active.
    - Press `4` and confirm tether breaks immediately.

UX guardrails implemented:
- In-game HUD is hidden when not joined (no overlap with login/select/create flows).
- `PLAY` is disabled for empty slots; character creation is required before joining world.
- Character select shows only filled slots plus one `+ Create Character` row for the next empty slot.
- Overwrite is not supported; delete is required before creating when slots are full.
- Deleting compacts characters upward so there is always only one visible empty slot entry.
- Character slot capacity in the current client UX is 6.

Class/spec note:
- Selection is persisted per account slot and sent on join.
- Server resolves ability profile per character at join/transfer.
- Server now fails fast at startup if any `content/specs/*.json` entry fails runtime profile compilation (no silent partial-load fallback).
- `ARMAMENT_REQUIRED_SPEC_ID` enforces that a specific spec must be present before server startup succeeds.
- Verify boot log contains your requested spec in `loaded=[...]` before testing a new kit.
- Content-authored playable specs:
  - `spec.bastion.bulwark`
  - `spec.bastion.cataclysm`
  - `spec.exorcist.warden` (includes status apply/consume, DR/shield, cleanse, taunt, and CC-slow baseline behavior)
  - `spec.exorcist.inquisitor` (includes DPS-tuned bound setup/payoff with cleanse utility and stronger burst windows)
  - `spec.gunslinger.akimbo` (includes fast dual-shot cadence, projectile/hitscan mix, and high-tempo spender windows)
  - `spec.gunslinger.deadeye` (includes ranged-leaning high-range slot tuning, drafted status apply, DR stance baseline)
  - `spec.dreadweaver.menace` (includes deterministic tether link create/tick/break behavior on `R` and `4`)
  - `spec.dreadweaver.deceiver` (includes DPS-tuned tether pressure, smoke-mark consume burst, and link-break finisher)
  - `spec.tidebinder.tidecaller` (includes authoritative `Heal` primitive and healing zones)
  - `spec.tidebinder.tempest` (includes DPS vortex/maelstrom zone damage and soaked-consuming burst)
  - `spec.arbiter.aegis` (includes constellation links, ally-oriented shields/heals/cleanse, and decree zone support)
  - `spec.arbiter.edict` (includes enemy-oriented links, decreed status consume burst, and lattice/decree damage zones)
- Unauthored specs transparently fall back to the configured server fallback profile.

## Class System Track

Class work is now tracked as a staged pipeline-first effort:

- `/Users/nckzvth/Projects/Armament/docs/classes/CLASS_SYSTEM_CONTRACT.md`
- `/Users/nckzvth/Projects/Armament/docs/classes/CAPABILITY_MATRIX.md`
- `/Users/nckzvth/Projects/Armament/docs/classes/IMPLEMENTATION_PLAN.md`
- `/Users/nckzvth/Projects/Armament/docs/classes/SPEC_TEMPLATE.md`
- `/Users/nckzvth/Projects/Armament/docs/classes/specs/README.md`

Rule:
- do not implement full class kits directly in sim loops.
- implement reusable primitives + AbilityRunner path first, then ship kits incrementally with deterministic tests.

## Animation Folder Layout (Unity)

Place class animation exports under:

- `/Users/nckzvth/Projects/Armament/client-unity/Assets/Art/Characters/<class-id>/Animations/`

Example (Bastion):
- `/Users/nckzvth/Projects/Armament/client-unity/Assets/Art/Characters/Bastion/Animations/bastion.locomotion`
- `/Users/nckzvth/Projects/Armament/client-unity/Assets/Art/Characters/Bastion/Animations/bastion.combat`

Importer/runtime pipeline:
- Editor auto-builds one library per class into `Assets/Resources/Animation/<class-id>.animationlibrary.asset`.
- Editor auto-builds/updates one map per class into `Assets/Resources/Animation/<class-id>.animationmap.asset`.
- Runtime local animator loads by authoritative `baseClassId` and uses these assets for loop/cast playback.

Do not place runtime assets in archive folders:
- any `*.archive` subfolder under class animation roots is excluded from the animation library build.

## Multi-Character Validation (Account Slots)

Use one account subject with different slots to verify separate characters:

1. In Unity, select the GameObject with `UdpGameClient`.
2. Set `Account Subject` to `local:dev-account`, `Character Slot` to `0`, `Character Name` to `Warrior`.
3. Play, earn currency, stop Play.
4. Keep same `Account Subject`, switch `Character Slot` to `1`, set `Character Name` to `Mage`.
5. Play and verify starting currency is independent from slot `0`.
6. Switch back to slot `0` and verify prior currency returns.

Expected behavior:
- slot `0` and slot `1` persist independently.
- `Character Name` can differ per slot and does not collide with other slots.
