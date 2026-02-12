# Architecture (Phase 4)

## Authoritative Model

- Server is the single source of truth.
- Client sends intent-only commands (`ClientHello`, `JoinOverworldRequest`, `InputCommand`).
- Server simulates Overworld at fixed tick and broadcasts snapshots.
- Active client implementation is MonoGame (`/Users/nckzvth/Projects/Armament/client-mg`) using the same protocol contracts from `shared-sim`.

## Tick and Snapshot Rates

- Simulation tick: `60 Hz`
- Snapshot cadence: `10 Hz` default
- Time step: fixed (`1/60` seconds)

## Zone/Instance Model

- `OverworldZone` exists as a single shared instance in Phase 0.
- Each connection can join once and receives an `EntityId`.
- Two connected clients appear in each other snapshots.
- Phase 4 slice adds `DungeonInstance` with separate per-instance simulation state and zone-aware snapshots.
- Client can transition zones (`F` interact/enter dungeon, `H` return overworld) and receives authoritative join-accepted messages.

## Protocol

- UDP only, no HTTP/CORS stack.
- Message types in `/shared-sim/Src/Protocol`.
- Basic handshake avoids amplification by only responding to valid `ClientHello` payloads.

## Prediction and Reconciliation

- Local client runs fixed-tick input prediction for owned entity.
- Server snapshots carry `LastProcessedInputSequence`; client replays unacked inputs after authoritative correction.
- Remote entities are rendered through an interpolation buffer with a short back-time window.

## Client Engine

- MonoGame client references `/Users/nckzvth/Projects/Armament/shared-sim/Src/Armament.SharedSim.csproj` directly.
- Unity client has been removed from this repository.

## Combat Slice (Phase 3)

- Input contract is encoded in `InputActionFlags`:
  - `FastAttackHold` (LMB), `HeavyAttackHold` (RMB), `BlockHold` (Shift)
  - `Skill1..Skill8` (E/R/Q/T/1/2/3/4)
  - `Pickup` (`Z` client keybind -> `InputActionFlags.Pickup`)
- Server remains authoritative for:
  - resource gain/spend
  - cooldown validation
  - enemy AI and damage resolution
  - loot spawn + pickup outcome
- Ability profile resolution is per character (not server-global):
  - join payload carries `BaseClassId` + `SpecId`
  - persistence stores class/spec per account slot
  - server resolves the active `SimAbilityProfile` per entity at runtime
  - dungeon/overworld transfers keep the same resolved profile id

## Determinism Scope

- Shared deterministic primitives seeded in `/shared-sim/Src/Determinism`.
- Shared deterministic movement sim now lives in `/shared-sim/Src/Sim`.
- Server `OverworldZone` steps `OverworldSimulator` directly (no server-only movement math path).
- Replay/hash determinism is validated with same-seed, same-input replay in shared-sim tests.

## Interaction Validation

- `F` interaction is carried in `InputCommand.ActionFlags` and validated server-side.
- Dungeon transition requires authoritative portal proximity (`OverworldZone.CanUseDungeonPortal`).
- Direct dungeon join request messages are rejected when out of range.

## Persistence Pipeline

- Durable persistence is implemented in `server-dotnet/Src/Persistence` using EF Core + PostgreSQL.
- `ArmamentDbContext` owns versioned schema + migrations.
- Repository boundaries:
  - `ICharacterRepository`
  - `IInventoryRepository`
  - `IQuestRepository`
  - `ILootTransactionService`
- `LootTransactionService` enforces anti-dupe semantics via unique pickup token + atomic transaction.
- Authoritative simulation remains non-blocking via a bounded async write queue:
  - tick loop emits loot grant events from sim state
  - server enqueues `LootPersistenceRequest` into `BoundedLootPersistenceQueue`
  - background worker performs transactional writes/retries without blocking simulation.
- Character join uses asynchronous profile load/create service:
  - join request enqueues profile load
  - server finalizes join only when profile result is available
  - loaded profile is applied to entity before first authoritative snapshot.
  - character identity is account-scoped (`accountSubject + characterSlot`), enabling multiple characters per account without name collisions.

## Security Milestones (Tracked)

Before MMO-scale expansion:
1. authenticated connect tokens
2. replay protection
3. packet authentication/signing
4. per-IP and per-session rate limiting
5. abuse-safe handshake hardening

## Class System Development Track

- Canonical contract and staged plan live in `/Users/nckzvth/Projects/Armament/docs/classes`.
- Development sequence is explicit:
  1. schema + validators
  2. deterministic AbilityRunner parity migration
  3. first anchor spec
  4. capability-driven expansion
- This prevents class drift and avoids spec-specific hardcoded combat paths.
