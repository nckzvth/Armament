#nullable enable
using System;

namespace Armament.SharedSim.Inventory;

public static class InventoryOps
{
    public static InventoryOpResult MoveBackpack(
        InventorySnapshot snapshot,
        IInventoryItemCatalog catalog,
        InventoryMoveRequest request)
    {
        InventoryRules.EnsureBackpackSize(snapshot);
        if (request.FromContainer == InventoryContainerKind.Backpack && request.ToContainer == InventoryContainerKind.Backpack)
        {
            if (!request.FromBackpackCell.HasValue || !request.ToBackpackCell.HasValue)
            {
                return InventoryOpResult.Fail("invalid cell");
            }

            return MoveBackpackToBackpack(snapshot, catalog, request.FromBackpackCell.Value, request.ToBackpackCell.Value, request.Quantity);
        }

        if (request.FromContainer == InventoryContainerKind.Backpack && request.ToContainer == InventoryContainerKind.Equipment)
        {
            if (request.ToEquipSlot is null)
            {
                return InventoryOpResult.Fail("invalid slot");
            }

            var sourceCell = ResolveSourceCell(snapshot, request.FromBackpackCell, -1);
            if (sourceCell is null)
            {
                return InventoryOpResult.Fail("invalid cell");
            }

            return BackpackToEquip(snapshot, catalog, new BackpackToEquipRequest
            {
                BackpackCell = sourceCell,
                TargetSlot = request.ToEquipSlot.Value
            });
        }

        if (request.FromContainer == InventoryContainerKind.Equipment && request.ToContainer == InventoryContainerKind.Backpack)
        {
            if (request.FromEquipSlot is null)
            {
                return InventoryOpResult.Fail("invalid slot");
            }

            return EquipToBackpack(snapshot, catalog, new EquipToBackpackRequest
            {
                SourceSlot = request.FromEquipSlot.Value,
                PreferredBackpackCell = request.ToBackpackCell
            });
        }

        if (request.FromContainer == InventoryContainerKind.Equipment && request.ToContainer == InventoryContainerKind.Equipment)
        {
            if (request.FromEquipSlot is null || request.ToEquipSlot is null)
            {
                return InventoryOpResult.Fail("invalid slot");
            }

            return MoveEquipToEquip(snapshot, catalog, request.FromEquipSlot.Value, request.ToEquipSlot.Value);
        }

        return InventoryOpResult.Fail("unsupported move");
    }

    public static InventoryOpResult DropFromBackpack(InventorySnapshot snapshot, IInventoryItemCatalog catalog, InventoryDropRequest request)
    {
        InventoryRules.EnsureBackpackSize(snapshot);
        var sourceCell = ResolveSourceCell(snapshot, request.BackpackCell, request.BackpackIndex);
        if (sourceCell is null)
        {
            return InventoryOpResult.Fail("invalid cell");
        }

        if (!InventoryRules.TryGetItemAtCell(snapshot, catalog, sourceCell.Value, out var item, out var index))
        {
            return InventoryOpResult.Fail("source empty");
        }

        var dropQty = request.Quantity <= 0 ? item.Quantity : Math.Min(item.Quantity, request.Quantity);
        item.Quantity -= dropQty;
        if (item.Quantity <= 0)
        {
            snapshot.BackpackItems.RemoveAt(index);
        }
        else
        {
            snapshot.BackpackItems[index] = item;
        }

        return InventoryOpResult.Ok();
    }

    public static InventoryOpResult BackpackToEquip(
        InventorySnapshot snapshot,
        IInventoryItemCatalog catalog,
        BackpackToEquipRequest request)
    {
        InventoryRules.EnsureBackpackSize(snapshot);
        var sourceCell = ResolveSourceCell(snapshot, request.BackpackCell, request.BackpackIndex);
        if (sourceCell is null)
        {
            return InventoryOpResult.Fail("invalid cell");
        }

        if (!InventoryRules.TryGetItemAtCell(snapshot, catalog, sourceCell.Value, out var source, out var sourceIndex))
        {
            return InventoryOpResult.Fail("source empty");
        }

        if (!catalog.TryGet(source.ItemCode, out var definition))
        {
            return InventoryOpResult.Fail("unknown item");
        }

        if (!InventoryRules.IsEquipmentSlotAllowed(definition, request.TargetSlot))
        {
            return InventoryOpResult.Fail("slot not allowed");
        }

        if (InventoryRules.ViolatesEquipUniqueKey(snapshot, catalog, definition, request.TargetSlot))
        {
            return InventoryOpResult.Fail("unique equip violated");
        }

        var equipped = CloneForEquip(source);
        var hadExisting = snapshot.Equipment.TryGetValue(request.TargetSlot, out var existing) && existing is not null;

        snapshot.Equipment[request.TargetSlot] = equipped;
        snapshot.BackpackItems.RemoveAt(sourceIndex);

        if (!hadExisting)
        {
            return InventoryOpResult.Ok();
        }

        var displaced = existing!;
        if (InventoryRules.CanPlaceAt(snapshot, catalog, displaced.ItemCode, source.Position))
        {
            displaced.Position = source.Position;
            snapshot.BackpackItems.Add(displaced);
            return InventoryOpResult.Ok();
        }

        if (InventoryRules.TryFindFirstFit(snapshot, catalog, displaced.ItemCode, out var fit))
        {
            displaced.Position = fit;
            snapshot.BackpackItems.Add(displaced);
            return InventoryOpResult.Ok();
        }

        snapshot.BackpackItems.Insert(sourceIndex, source);
        snapshot.Equipment[request.TargetSlot] = existing;
        return InventoryOpResult.Fail("backpack full");
    }

    public static InventoryOpResult EquipToBackpack(
        InventorySnapshot snapshot,
        IInventoryItemCatalog catalog,
        EquipToBackpackRequest request)
    {
        if (!snapshot.Equipment.TryGetValue(request.SourceSlot, out var source) || source is null)
        {
            return InventoryOpResult.Fail("source empty");
        }

        var preferredCell = ResolveSourceCell(snapshot, request.PreferredBackpackCell, request.PreferredBackpackIndex ?? -1);
        if (preferredCell.HasValue)
        {
            if (!InventoryRules.CanPlaceAt(snapshot, catalog, source.ItemCode, preferredCell.Value))
            {
                return InventoryOpResult.Fail("target occupied");
            }

            source.Position = preferredCell.Value;
            snapshot.BackpackItems.Add(source);
            snapshot.Equipment[request.SourceSlot] = null;
            return InventoryOpResult.Ok();
        }

        if (!InventoryRules.TryFindFirstFit(snapshot, catalog, source.ItemCode, out var fit))
        {
            return InventoryOpResult.Fail("backpack full");
        }

        source.Position = fit;
        snapshot.BackpackItems.Add(source);
        snapshot.Equipment[request.SourceSlot] = null;
        return InventoryOpResult.Ok();
    }

    public static InventoryOpResult QuickEquip(InventorySnapshot snapshot, IInventoryItemCatalog catalog, int backpackIndex)
    {
        if (!InventoryRules.TryBackpackCellFromIndex(snapshot, backpackIndex, out var sourceCell))
        {
            return InventoryOpResult.Fail("invalid index");
        }

        if (!InventoryRules.TryGetItemAtCell(snapshot, catalog, sourceCell, out var source, out _))
        {
            return InventoryOpResult.Fail("source empty");
        }

        if (!catalog.TryGet(source.ItemCode, out var def) || def.EquipSlots.Count == 0)
        {
            return InventoryOpResult.Fail("item not equippable");
        }

        foreach (var slot in InventoryRules.EnumerateEquipPriority(def))
        {
            var result = BackpackToEquip(snapshot, catalog, new BackpackToEquipRequest
            {
                BackpackCell = sourceCell,
                TargetSlot = slot
            });

            if (result.Success)
            {
                return result;
            }
        }

        return InventoryOpResult.Fail("no eligible slot");
    }

    public static InventoryOpResult QuickUnequip(InventorySnapshot snapshot, IInventoryItemCatalog catalog, EquipSlot slot)
    {
        return EquipToBackpack(snapshot, catalog, new EquipToBackpackRequest
        {
            SourceSlot = slot,
            PreferredBackpackCell = null
        });
    }

    public static InventoryOpResult AddToBackpack(InventorySnapshot snapshot, IInventoryItemCatalog catalog, InventoryItemStack stack)
    {
        InventoryRules.EnsureBackpackSize(snapshot);
        if (stack.Quantity <= 0 || string.IsNullOrWhiteSpace(stack.ItemCode))
        {
            return InventoryOpResult.Fail("invalid stack");
        }

        if (!catalog.TryGet(stack.ItemCode, out var definition))
        {
            return InventoryOpResult.Fail("unknown item");
        }

        var maxStack = Math.Max(1, definition.MaxStack);
        var remaining = stack.Quantity;
        if (maxStack > 1)
        {
            for (var i = 0; i < snapshot.BackpackItems.Count && remaining > 0; i++)
            {
                var item = snapshot.BackpackItems[i];
                if (!string.Equals(item.ItemCode, stack.ItemCode, StringComparison.Ordinal) || item.Quantity >= maxStack)
                {
                    continue;
                }

                var add = Math.Min(maxStack - item.Quantity, remaining);
                item.Quantity += add;
                snapshot.BackpackItems[i] = item;
                remaining -= add;
            }
        }

        while (remaining > 0)
        {
            var placeQty = Math.Min(maxStack, remaining);
            if (!InventoryRules.TryFindFirstFit(snapshot, catalog, stack.ItemCode, out var placement))
            {
                return InventoryOpResult.Fail("backpack full");
            }

            snapshot.BackpackItems.Add(new InventoryItemInstance
            {
                InstanceId = Guid.NewGuid().ToString("N"),
                ItemCode = stack.ItemCode,
                Quantity = placeQty,
                Position = placement
            });
            remaining -= placeQty;
        }

        return InventoryOpResult.Ok();
    }

    private static InventoryOpResult MoveBackpackToBackpack(
        InventorySnapshot snapshot,
        IInventoryItemCatalog catalog,
        GridCoord fromCell,
        GridCoord toCell,
        int quantity)
    {
        if (!InventoryRules.TryGetItemAtCell(snapshot, catalog, fromCell, out var source, out var sourceIndex))
        {
            return InventoryOpResult.Fail("source empty");
        }

        if (!catalog.TryGet(source.ItemCode, out var sourceDef))
        {
            return InventoryOpResult.Fail("unknown item");
        }

        if (!InventoryRules.CanPlaceAt(snapshot, catalog, source.ItemCode, toCell, source.InstanceId))
        {
            // Optional stack-merge behavior for 1x1 stackables.
            if (sourceDef.MaxStack > 1 &&
                InventoryRules.ResolveItemWidth(sourceDef) == 1 &&
                InventoryRules.ResolveItemHeight(sourceDef) == 1 &&
                InventoryRules.TryGetItemAtCell(snapshot, catalog, toCell, out var target, out var targetIndex) &&
                string.Equals(target.ItemCode, source.ItemCode, StringComparison.Ordinal))
            {
                var room = Math.Max(0, sourceDef.MaxStack - target.Quantity);
                if (room <= 0)
                {
                    return InventoryOpResult.Fail("target full");
                }

                var moveQty = quantity > 0 ? Math.Min(quantity, source.Quantity) : source.Quantity;
                moveQty = Math.Min(moveQty, room);
                source.Quantity -= moveQty;
                target.Quantity += moveQty;
                snapshot.BackpackItems[targetIndex] = target;
                if (source.Quantity <= 0)
                {
                    snapshot.BackpackItems.RemoveAt(sourceIndex);
                }
                else
                {
                    snapshot.BackpackItems[sourceIndex] = source;
                }

                return InventoryOpResult.Ok();
            }

            return InventoryOpResult.Fail("target occupied");
        }

        if (sourceDef.MaxStack > 1 && quantity > 0 && quantity < source.Quantity)
        {
            var splitQty = Math.Min(quantity, source.Quantity);
            source.Quantity -= splitQty;
            snapshot.BackpackItems[sourceIndex] = source;
            snapshot.BackpackItems.Add(new InventoryItemInstance
            {
                InstanceId = Guid.NewGuid().ToString("N"),
                ItemCode = source.ItemCode,
                Quantity = splitQty,
                Position = toCell
            });
            return InventoryOpResult.Ok();
        }

        source.Position = toCell;
        snapshot.BackpackItems[sourceIndex] = source;
        return InventoryOpResult.Ok();
    }

    private static InventoryOpResult MoveEquipToEquip(
        InventorySnapshot snapshot,
        IInventoryItemCatalog catalog,
        EquipSlot fromSlot,
        EquipSlot toSlot)
    {
        if (!snapshot.Equipment.TryGetValue(fromSlot, out var source) || source is null)
        {
            return InventoryOpResult.Fail("source empty");
        }

        if (!catalog.TryGet(source.ItemCode, out var sourceDef) || !InventoryRules.IsEquipmentSlotAllowed(sourceDef, toSlot))
        {
            return InventoryOpResult.Fail("slot not allowed");
        }

        if (InventoryRules.ViolatesEquipUniqueKey(snapshot, catalog, sourceDef, toSlot))
        {
            return InventoryOpResult.Fail("unique equip violated");
        }

        var target = snapshot.Equipment.TryGetValue(toSlot, out var t) ? t : null;
        if (target is not null)
        {
            if (!catalog.TryGet(target.ItemCode, out var targetDef) || !InventoryRules.IsEquipmentSlotAllowed(targetDef, fromSlot))
            {
                return InventoryOpResult.Fail("target cannot swap");
            }
        }

        snapshot.Equipment[toSlot] = source;
        snapshot.Equipment[fromSlot] = target;
        return InventoryOpResult.Ok();
    }

    private static GridCoord? ResolveSourceCell(InventorySnapshot snapshot, GridCoord? cell, int legacyIndex)
    {
        if (cell.HasValue)
        {
            return cell;
        }

        if (legacyIndex >= 0 && InventoryRules.TryBackpackCellFromIndex(snapshot, legacyIndex, out var translated))
        {
            return translated;
        }

        return null;
    }

    private static InventoryItemInstance CloneForEquip(InventoryItemInstance source)
    {
        return new InventoryItemInstance
        {
            InstanceId = string.IsNullOrWhiteSpace(source.InstanceId) ? Guid.NewGuid().ToString("N") : source.InstanceId,
            ItemCode = source.ItemCode,
            Quantity = 1,
            Position = source.Position
        };
    }
}
