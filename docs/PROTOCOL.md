# Protocol (Phase 3 Slice)

## Transport

- UDP datagrams
- Message serialization: shared binary codec (`ProtocolCodec`)

## Message Flow

1. `ClientHello` -> `ServerHello`
2. `JoinOverworldRequest` -> `JoinOverworldAccepted`
3. optional `JoinDungeonRequest` -> `JoinDungeonAccepted`
4. repeating `InputCommand`
5. repeating `WorldSnapshot`

## Input and Snapshot Semantics

- `InputCommand` includes:
  - `Sequence` (monotonic client input sequence)
  - `ClientTick` (client-side fixed tick index)
  - quantized movement axes
  - `ActionFlags` (`FastAttackHold`, `HeavyAttackHold`, `BlockHold`, `Skill1..Skill8`, `Pickup`)
- `WorldSnapshot` includes:
  - `ServerTick`
  - `LastProcessedInputSequence` (ack for recipient client reconciliation)
  - `ZoneKind` (`Overworld` or `Dungeon`)
  - `InstanceId` (0 for overworld, non-zero for dungeon instances)
  - entity list with:
    - `EntityId`
    - `Kind` (`Player`, `Enemy`, `Loot`)
    - quantized position
    - `Health`
    - `BuilderResource`
    - `SpenderResource`
    - `Currency`

## Quantization

- Position scale: `100`
- Input scale: `1000`
- Encoded as signed 16-bit values
