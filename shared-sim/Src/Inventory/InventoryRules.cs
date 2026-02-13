#nullable enable
using System;
using System.Collections.Generic;

namespace Armament.SharedSim.Inventory;

public static class InventoryRules
{
    public static int GetBackpackCapacity(InventorySnapshot snapshot)
    {
        return Math.Max(1, snapshot.BackpackWidth) * Math.Max(1, snapshot.BackpackHeight);
    }

    public static bool IsBackpackIndexValid(InventorySnapshot snapshot, int index)
    {
        return index >= 0 && index < GetBackpackCapacity(snapshot);
    }

    public static bool TryBackpackIndexFromCell(InventorySnapshot snapshot, GridCoord cell, out int index)
    {
        index = -1;
        if (cell.X < 0 || cell.Y < 0 || cell.X >= Math.Max(1, snapshot.BackpackWidth) || cell.Y >= Math.Max(1, snapshot.BackpackHeight))
        {
            return false;
        }

        index = cell.Y * Math.Max(1, snapshot.BackpackWidth) + cell.X;
        return true;
    }

    public static bool TryBackpackCellFromIndex(InventorySnapshot snapshot, int index, out GridCoord cell)
    {
        cell = default;
        if (!IsBackpackIndexValid(snapshot, index))
        {
            return false;
        }

        var width = Math.Max(1, snapshot.BackpackWidth);
        cell = new GridCoord
        {
            X = index % width,
            Y = index / width
        };
        return true;
    }

    public static bool IsEquipmentSlotAllowed(InventoryItemDefinition definition, EquipSlot slot)
    {
        if (IsAccessorySlot(slot) && HasAnyAccessorySlot(definition))
        {
            return true;
        }

        return definition.EquipSlots.Contains(slot);
    }

    public static bool ViolatesEquipUniqueKey(
        InventorySnapshot snapshot,
        IInventoryItemCatalog catalog,
        InventoryItemDefinition candidate,
        EquipSlot targetSlot)
    {
        if (string.IsNullOrWhiteSpace(candidate.EquipUniqueKey))
        {
            return false;
        }

        foreach (var kvp in snapshot.Equipment)
        {
            if (kvp.Key == targetSlot || kvp.Value is null)
            {
                continue;
            }

            if (!catalog.TryGet(kvp.Value.ItemCode, out var equippedDef))
            {
                continue;
            }

            if (string.Equals(candidate.EquipUniqueKey, equippedDef.EquipUniqueKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static void EnsureBackpackSize(InventorySnapshot snapshot)
    {
        var capacity = GetBackpackCapacity(snapshot);
        if (snapshot.BackpackItems.Count == 0 && snapshot.Backpack.Count > 0)
        {
            // Backward compatibility path: convert legacy flat-slot backpack into 1x1 instances.
            for (var i = 0; i < snapshot.Backpack.Count && i < capacity; i++)
            {
                var legacy = snapshot.Backpack[i];
                if (legacy is null || legacy.Value.Quantity <= 0 || string.IsNullOrWhiteSpace(legacy.Value.ItemCode))
                {
                    continue;
                }

                if (!TryBackpackCellFromIndex(snapshot, i, out var cell))
                {
                    continue;
                }

                snapshot.BackpackItems.Add(new InventoryItemInstance
                {
                    InstanceId = $"legacy-{i}",
                    ItemCode = legacy.Value.ItemCode,
                    Quantity = legacy.Value.Quantity,
                    Position = cell
                });
            }
        }

        snapshot.Backpack.Clear();
        while (snapshot.Backpack.Count < capacity)
        {
            snapshot.Backpack.Add(null);
        }

        var width = Math.Max(1, snapshot.BackpackWidth);
        var height = Math.Max(1, snapshot.BackpackHeight);
        snapshot.BackpackItems.RemoveAll(item =>
            item.Quantity <= 0 ||
            string.IsNullOrWhiteSpace(item.ItemCode) ||
            item.Position.X < 0 ||
            item.Position.Y < 0 ||
            item.Position.X >= width ||
            item.Position.Y >= height);
    }

    public static IEnumerable<EquipSlot> EnumerateEquipPriority(InventoryItemDefinition definition)
    {
        for (var i = 0; i < definition.EquipSlots.Count; i++)
        {
            yield return definition.EquipSlots[i];
        }
    }

    private static bool IsAccessorySlot(EquipSlot slot)
    {
        return slot == EquipSlot.Ring1 || slot == EquipSlot.Ring2 || slot == EquipSlot.Amulet || slot == EquipSlot.Relic;
    }

    private static bool HasAnyAccessorySlot(InventoryItemDefinition definition)
    {
        for (var i = 0; i < definition.EquipSlots.Count; i++)
        {
            if (IsAccessorySlot(definition.EquipSlots[i]))
            {
                return true;
            }
        }

        return false;
    }

    public static int ResolveItemWidth(InventoryItemDefinition definition)
    {
        return Math.Max(1, definition.GridWidth);
    }

    public static int ResolveItemHeight(InventoryItemDefinition definition)
    {
        return Math.Max(1, definition.GridHeight);
    }

    public static bool TryGetItemAtCell(
        InventorySnapshot snapshot,
        IInventoryItemCatalog catalog,
        GridCoord cell,
        out InventoryItemInstance item,
        out int itemIndex)
    {
        item = new InventoryItemInstance();
        itemIndex = -1;
        for (var i = 0; i < snapshot.BackpackItems.Count; i++)
        {
            var candidate = snapshot.BackpackItems[i];
            if (!catalog.TryGet(candidate.ItemCode, out var definition))
            {
                continue;
            }

            var width = ResolveItemWidth(definition);
            var height = ResolveItemHeight(definition);
            if (CellWithinItem(cell, candidate.Position, width, height))
            {
                item = candidate;
                itemIndex = i;
                return true;
            }
        }

        return false;
    }

    public static bool CellWithinItem(GridCoord cell, GridCoord itemPos, int width, int height)
    {
        return cell.X >= itemPos.X &&
               cell.Y >= itemPos.Y &&
               cell.X < itemPos.X + width &&
               cell.Y < itemPos.Y + height;
    }

    public static bool CanPlaceAt(
        InventorySnapshot snapshot,
        IInventoryItemCatalog catalog,
        string itemCode,
        GridCoord topLeft,
        string? ignoreInstanceId = null)
    {
        if (!catalog.TryGet(itemCode, out var definition))
        {
            return false;
        }

        var width = ResolveItemWidth(definition);
        var height = ResolveItemHeight(definition);
        return CanPlaceAt(snapshot, catalog, topLeft, width, height, ignoreInstanceId);
    }

    public static bool CanPlaceAt(
        InventorySnapshot snapshot,
        IInventoryItemCatalog catalog,
        GridCoord topLeft,
        int width,
        int height,
        string? ignoreInstanceId = null)
    {
        var backpackWidth = Math.Max(1, snapshot.BackpackWidth);
        var backpackHeight = Math.Max(1, snapshot.BackpackHeight);
        if (topLeft.X < 0 || topLeft.Y < 0 || topLeft.X + width > backpackWidth || topLeft.Y + height > backpackHeight)
        {
            return false;
        }

        var x0 = topLeft.X;
        var y0 = topLeft.Y;
        var x1 = x0 + width - 1;
        var y1 = y0 + height - 1;

        for (var i = 0; i < snapshot.BackpackItems.Count; i++)
        {
            var other = snapshot.BackpackItems[i];
            if (!string.IsNullOrEmpty(ignoreInstanceId) &&
                string.Equals(other.InstanceId, ignoreInstanceId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!catalog.TryGet(other.ItemCode, out var otherDef))
            {
                continue;
            }

            var ow = ResolveItemWidth(otherDef);
            var oh = ResolveItemHeight(otherDef);
            var ox0 = other.Position.X;
            var oy0 = other.Position.Y;
            var ox1 = ox0 + ow - 1;
            var oy1 = oy0 + oh - 1;

            var overlap = x0 <= ox1 && x1 >= ox0 && y0 <= oy1 && y1 >= oy0;
            if (overlap)
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryFindFirstFit(
        InventorySnapshot snapshot,
        IInventoryItemCatalog catalog,
        string itemCode,
        out GridCoord cell)
    {
        cell = default;
        if (!catalog.TryGet(itemCode, out var definition))
        {
            return false;
        }

        var width = ResolveItemWidth(definition);
        var height = ResolveItemHeight(definition);
        var backpackWidth = Math.Max(1, snapshot.BackpackWidth);
        var backpackHeight = Math.Max(1, snapshot.BackpackHeight);
        for (var y = 0; y <= backpackHeight - height; y++)
        {
            for (var x = 0; x <= backpackWidth - width; x++)
            {
                var candidate = new GridCoord { X = x, Y = y };
                if (CanPlaceAt(snapshot, catalog, candidate, width, height))
                {
                    cell = candidate;
                    return true;
                }
            }
        }

        return false;
    }
}
