# MonoGame Migration Complete

Unity client replacement is complete in this repository. Armament now ships as MonoGame client + authoritative .NET server + shared deterministic contracts.

## Completed

- Added `/Users/nckzvth/Projects/Armament/client-mg/Armament.Client.MonoGame` on .NET 9 + MonoGame 3.8.4.1.
- Direct project reference from MonoGame client to `/Users/nckzvth/Projects/Armament/shared-sim/Src/Armament.SharedSim.csproj`.
- Implemented protocol-compatible client loop:
  - `ClientHello` -> `JoinOverworldRequest` -> snapshot stream
  - fixed-tick input command send (60Hz)
  - action flags and sequencing maintained
- Implemented client simulation behavior parity slice:
  - local prediction and reconciliation from `LastProcessedInputSequence`
  - remote interpolation buffer
- Implemented debug renderer parity slice:
  - player/enemy/loot/zone/link primitives
  - HUD and combat/cast feeds
  - class/spec selection controls and persisted config
- Updated verification scripts to validate MonoGame client build instead of Unity batch tests.
- Removed Unity client directory and Unity runner scripts from this repository.
- Updated CI workflow to MonoGame/.NET-only checks.

## Current Runtime Entry Points

- Server:
  - `dotnet run --project /Users/nckzvth/Projects/Armament/server-dotnet/Src/ServerHost/Armament.ServerHost.csproj -- --port 9000 --simulation-hz 60 --snapshot-hz 10`
- Client (macOS/Linux):
  - `/Users/nckzvth/Projects/Armament/ops/scripts/run-client-mg.sh`
- Client (Windows):
  - `/Users/nckzvth/Projects/Armament/ops/scripts/run-client-mg.ps1`

## Parity Scope (explicit)

Parity is currently validated for **network/gameplay control flow**, not final art/VFX presentation:

- Connect/join/session flow
- Movement and combat input command pipeline
- Snapshot visualization and zone transitions
- Authoritative cast result visibility

## Post-Migration Enhancements

1. Replace debug primitive rendering with production sprite/atlas pipeline in MonoGame.
2. Expand MonoGame animation system from debug state machine to full class animation library parity.
3. Add MonoGame client integration tests (headless-friendly contract tests around protocol/prediction modules).

## Hosting Notes (post-migration)

For friend playtesting during development:

1. Run dedicated `ServerHost` on a cloud VM.
2. Open UDP game port (default `9000`) in host firewall/security group.
3. Keep Postgres persistent storage separate from process lifecycle.
4. Add pre-auth/connect token milestone before opening to broader audience.
