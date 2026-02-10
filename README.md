# Armament Foundation (Phase 4 Slice)

Authoritative UDP Unity/.NET ARPG foundation with shared deterministic sim, prediction/reconciliation, interpolation, combat controls, one enemy archetype, loot drops, and pickup.

## Repo Layout

- `/Users/nckzvth/Projects/Armament/client-unity` Unity 6 project
- `/Users/nckzvth/Projects/Armament/shared-sim` netstandard2.1 shared protocol/deterministic sim
- `/Users/nckzvth/Projects/Armament/server-dotnet` authoritative server host/game loop/transport
- `/Users/nckzvth/Projects/Armament/ops/scripts` verification and Unity batch runners
- `/Users/nckzvth/Projects/Armament/docs` architecture/protocol/determinism/persistence notes

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

## Play In Unity (Manual Evidence)

1. Open `/Users/nckzvth/Projects/Armament/client-unity` in Unity 6.
2. Press Play.
3. Verify the debug HUD appears in top-left (`Armament Phase 3 Debug HUD`).
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
4. server smoke test (2 clients join, snapshots exchanged, enemy archetype present, combat loop affects state)
5. persistence integration test:
   - applies EF Core migrations
   - validates create/load character
   - validates transactional loot pickup with duplicate prevention
6. Unity batch tests (EditMode + PlayMode)

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
2. Unity editor play mode: move and attack enemy until yellow loot appears.
3. Move into pickup range of the purple `Test Relic` and press `Z`; confirm it despawns (manual pickup path).
4. Walk into yellow gold drop radius and confirm `Currency` increments automatically.
5. Press `Option`/`Alt` twice and confirm drop names toggle on then off.
6. After loot is claimed/auto-looted, confirm the yellow drop visual despawns immediately (no enlarged ghost square remains).
7. Move near portal and confirm `[F] Dungeon` prompt appears above it, then press `F` to enter dungeon and confirm HUD zone switches to `Dungeon` with a non-zero instance id.
8. Defeat dungeon enemies and confirm higher reward drop behavior.
9. Press `H` and confirm return to overworld.
10. Hold `Shift` while enemy attacks and confirm HP drops slower than without block.
11. Stop and restart server with the same `ARMAMENT_DB_CONNECTION`, rejoin with same character name, and confirm previously earned currency persists.
12. Run `./ops/scripts/verify.sh` and ensure all checks pass.

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
