# MonoGame Migration Complete

Unity client replacement is complete in this repository. Armament now runs as MonoGame client + authoritative .NET server + shared deterministic contracts.

## Completed Scope

- MonoGame DesktopGL client in `/Users/nckzvth/Projects/Armament/client-mg/Armament.Client.MonoGame`.
- Direct project reference to `/Users/nckzvth/Projects/Armament/shared-sim/Src/Armament.SharedSim.csproj`.
- Authoritative protocol flow parity:
  - `ClientHello` -> `JoinOverworldRequest` -> snapshot stream.
  - fixed-tick input command send (60Hz) with sequence/tick values.
- Gameplay control parity:
  - movement, combat input flags, interact/loot/return flows.
  - local prediction + reconciliation from `LastProcessedInputSequence`.
  - remote interpolation.
- Session parity:
  - Overworld/Dungeon transitions.
  - account/slot/class/spec selection and persistence on client side.
- Runtime HUD + debug feeds for authoritative outcomes.
- MonoGame animation runtime loader path implemented (class/spec-aware clip map resolution).
- Validation/CI migration complete:
  - Unity checks removed.
  - MonoGame build + MonoGame client logic tests in verify pipeline.
  - Atlas validation tool added: `/Users/nckzvth/Projects/Armament/ops/tools/AtlasValidator`.
- Unity client removed from repo.

## Explicitly Out Of Scope (per current request)

- Packaging/distribution automation for friend playtests.

## Runtime Entry Points

- Server:
  - `dotnet run --project /Users/nckzvth/Projects/Armament/server-dotnet/Src/ServerHost/Armament.ServerHost.csproj -- --port 9000 --simulation-hz 60 --snapshot-hz 10`
- Client (macOS/Linux):
  - `/Users/nckzvth/Projects/Armament/ops/scripts/run-client-mg.sh`
- Single command dev loop:
  - `/Users/nckzvth/Projects/Armament/ops/scripts/run-dev.sh`

## Verification Contract

- macOS/Linux:
  - `/Users/nckzvth/Projects/Armament/ops/scripts/verify.sh`
- Windows:
  - `/Users/nckzvth/Projects/Armament/ops/scripts/verify.ps1`

Verify includes:
- formatting checks (shared-sim, server, client-mg)
- shared sim tests + content validation
- atlas frame bounds validation
- server tests + persistence integration
- MonoGame build + MonoGame client logic tests
