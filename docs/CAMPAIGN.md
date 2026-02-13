# Campaign Execution Plan

## Purpose

Define the implementation path for campaign systems and Act rollout with strict alignment to Armament architecture.

Detailed Act 1 design/content lives in:

- `docs/campaign/ACT1_CONTENT_SPEC.md`

## Repository Baseline

Armament already has:

- authoritative server with deterministic fixed-step simulation
- MonoGame client replacing Unity
- shared protocol/sim contracts across server/client
- class/spec content pipeline with validation and verification gates
- persistence + migrations + non-blocking server integration

## Prerequisites Before Full Act Expansion

These are required to prevent campaign implementation from devolving into bespoke simulator code:

1. World content pack contract under `content/world/...`
2. World-content validation in `verify` (schema + references + invariants)
3. Content-driven zone/link/hazard definitions (not hardcoded template switches)
4. Deterministic sim event buffer for quest/encounter/objective progression
5. First-class world object state in shared sim
6. Typed replication for enemy/object/hazard/objective identity

## Migration-Adjacent Debt Status

This phase is now resolved and should be treated as contract, not optional cleanup:

1. Client decomposition
- `ArmamentGame.cs` is now split with dedicated files for client infrastructure and quest log logic.
- New campaign/client features should continue this pattern rather than re-concentrating orchestration in one file.

2. Replication typing
- Campaign replication is explicit for:
  - enemy identity (`EntitySnapshot.ArchetypeId`)
  - world zones (`WorldZoneSnapshot`)
  - links (`WorldLinkSnapshot`)
  - world objects/hazards/objectives (typed snapshots)
- Do not encode zone/link semantics into generic `EntitySnapshot` fields.

## Non-Negotiables

- server authoritative outcomes only
- deterministic simulation path only
- content drift must fail verification
- no per-zone bespoke logic when reusable systems can represent behavior
- campaign overworld must not spawn placeholder/debug enemies
- campaign objective snapshots must be available immediately after character join/load
- in campaign mode, enemy archetype identity must come from authored encounter content

## Runtime Contracts (Must Hold)

1. Campaign encounter activation
- Active encounters are derived from quest state and online character state.
- Encounter enemies/objects/hazards are instantiated from content definitions and map layout data.

2. Quest/objective visibility
- `BuildObjectiveSnapshots` must return deterministic objective data for joined characters on first snapshot.
- Absence of prior persisted quest state must not hide objectives for the first active quest.
- `TalkToNpc` objectives must complete from runtime interaction events, never via bootstrap auto-complete.

3. Overworld spawn policy
- In campaign mode, no fallback enemy should be auto-spawned at player join.
- Any fallback enemy spawn logic is allowed only when campaign world content is not active.

4. Typed snapshot policy
- Zones and links must replicate via typed payloads (`WorldZoneSnapshot`, `WorldLinkSnapshot`).
- `EntitySnapshot` remains for actors and loot, not for synthetic zone/link encoding.
- Campaign NPCs replicate via typed payloads (`WorldNpcSnapshot`) with stable authored IDs.

## Architecture Recommendations

1. Add world content pack:
- `content/world/acts/act1/act.json`
- `content/world/acts/act1/zones/*.json`
- `content/world/acts/act1/quests/*.json`
- `content/world/acts/act1/encounters/*.json`
- `content/world/bestiary/*.json`
- `content/world/objects/*.json`
- `content/world/hazards/*.json`

2. Add world-content validation and compilation gates:
- schema checks
- reference integrity
- id uniqueness
- enum/invariant checks
- startup compile/load fail-fast

3. Add deterministic `SimEventBuffer` in shared sim for:
- enemy kills
- object destruction
- objective completion
- token/objective pickup
- region entry

4. Promote content-driven zone/link/hazard runtime definitions.

5. Add `SimWorldObjectState` for objective/spawner/interactable objects.

6. Add explicit replication representations for campaign identity/state.

## Milestone Plan

Current status:

- Milestone A: in progress (foundation scaffolding + validation/load gates landed)
- Milestone B: in progress (content-driven zone/link definitions wired; hazards/object runtime still pending)

### Milestone A: World Content Foundation

Deliver:

- world schemas
- world-content validator tests
- server world content loader

Exit criteria:

- invalid world content fails local verify and CI

### Milestone B: Zones/Links/Hazards Become Content-Driven

Deliver:

- expanded zone/link content fields
- runtime definition loading path
- removal of hardcoded template switching

Exit criteria:

- hazards and zone pulses are authored data

### Milestone C: Bestiary + AI Brains

Deliver:

- bestiary definitions
- deterministic AI brain profiles
- enemy archetype replication identity

Exit criteria:

- enemy behavior chosen by data definitions

### Milestone D: Objects + Encounter Runtime

Deliver:

- world object sim state
- encounter/objective state machine runtime
- object-linked mechanics

Exit criteria:

- CP/IG objective patterns represented without bespoke sim branches

### Milestone E: Quest/Gate Framework

Deliver:

- server-authoritative quest runtime
- progression gates + persistence updates

Exit criteria:

- Act progression chain deterministic and persisted

### Milestone F: Cathedral Floor Framework

Deliver:

- CC1/CC2/CC3 floor definitions
- floor objective/boss progression runtime

Exit criteria:

- full floor ladder authored via content

## Immediate Next Steps

1. Scaffold `content/world/...` and baseline Act 1 pack files.
2. Add world-content validator test project and wire into verify scripts.
3. Implement minimal `SimEventBuffer` + object state in shared sim.
4. Implement Camp Perimeter as first authored slice on those systems.
5. Keep quest-loot path aligned with authoritative grid inventory/equipment contract:
`docs/campaign/INVENTORY_MIGRATION.md`.

## Required Verification Gate

Every campaign runtime handoff must pass all checks below:

```bash
cd /Users/nckzvth/Projects/Armament

dotnet build shared-sim/Src/Armament.SharedSim.csproj
dotnet build server-dotnet/Src/ServerHost/Armament.ServerHost.csproj
dotnet build client-mg/Armament.Client.MonoGame/Armament.Client.MonoGame.csproj

dotnet run --project shared-sim/Tests/WorldContentValidation.Tests/Armament.WorldContentValidation.Tests.csproj
dotnet run --project shared-sim/Tests/SharedSim.Tests/Armament.SharedSim.Tests.csproj
dotnet run --project server-dotnet/Tests/GameServer.Tests/Armament.GameServer.Tests.csproj
dotnet run --project server-dotnet/Tests/Persistence.Tests/Armament.Persistence.Tests.csproj
dotnet test client-mg/Tests/Armament.Client.MonoGame.Tests/Armament.Client.MonoGame.Tests.csproj
```
