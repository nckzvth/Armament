# Inventory Migration Contract

## Purpose

Keep campaign/quest loot implementation aligned with an authoritative MMO inventory/equipment model.

This document maps the prior TypeScript structure you referenced to the new C# structure in Armament.

## Required Shape (authoritative)

- Server is source of truth for backpack + equipment.
- Client is presentation + input only.
- Shared layer owns inventory contracts/rules/ops (pure logic).
- Persistence stores authoritative inventory snapshot JSON per character.

## C# Mapping

### Shared contracts/rules/ops

- `/Users/nckzvth/Projects/Armament/shared-sim/Src/Inventory/InventoryContracts.cs`
  - `EquipSlot`
  - `InventorySnapshot` (grid layout: `BackpackWidth`, `BackpackHeight`, cell-indexed backpack)
  - move/drop/equip request DTOs
- `/Users/nckzvth/Projects/Armament/shared-sim/Src/Inventory/InventoryRules.cs`
  - grid index/cell conversion
  - equip slot validation
  - unique-equip enforcement
- `/Users/nckzvth/Projects/Armament/shared-sim/Src/Inventory/InventoryOps.cs`
  - backpack<->backpack grid moves
  - backpack<->equip moves
  - equip<->equip swaps
  - quick equip/unequip
  - drop + add-stack behavior

### Server-authoritative service scaffold

- `/Users/nckzvth/Projects/Armament/server-dotnet/Src/GameServer/Inventory/AuthoritativeInventoryService.cs`
  - authoritative operations over per-character inventory state
  - grant/move/drop/equip/quick-equip entrypoints

### JSON snapshot codec

- `/Users/nckzvth/Projects/Armament/shared-sim/Src/Inventory/InventoryJsonCodec.cs`

## Campaign Integration Points

When quest/objective loot is implemented:

1. Loot grant path resolves item code/quantity.
2. Server calls authoritative inventory service (not client).
3. Updated inventory snapshot is persisted back to character `InventoryJson`.
4. Snapshot/update message is sent to client for UI refresh.

## Next required work (not deferred)

1. Add inventory protocol messages in shared protocol for snapshot + move/drop/equip requests.
2. Wire server request handlers to `AuthoritativeInventoryService`.
3. Add persistence write-back path for `InventoryJson` updates.
4. Add MonoGame inventory/equipment UI with drag/drop + quick equip input.

