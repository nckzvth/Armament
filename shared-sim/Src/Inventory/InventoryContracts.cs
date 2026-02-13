#nullable enable
using System;
using System.Collections.Generic;

namespace Armament.SharedSim.Inventory;

public enum EquipSlot : byte
{
    Head = 1,
    Chest = 2,
    Hands = 3,
    Legs = 4,
    Feet = 5,
    MainHand = 6,
    OffHand = 7,
    Amulet = 8,
    Ring1 = 9,
    Ring2 = 10,
    Belt = 11,
    Relic = 12
}

public sealed class InventoryItemDefinition
{
    public string ItemCode { get; set; } = string.Empty;
    public int MaxStack { get; set; } = 1;
    public int GridWidth { get; set; } = 1;
    public int GridHeight { get; set; } = 1;
    public List<EquipSlot> EquipSlots { get; set; } = new();
    public string? EquipUniqueKey { get; set; }
}

public struct InventoryItemStack
{
    public string ItemCode { get; set; }
    public int Quantity { get; set; }
}

public sealed class InventorySnapshot
{
    public int BackpackWidth { get; set; } = 10;
    public int BackpackHeight { get; set; } = 8;
    public List<InventoryItemStack?> Backpack { get; set; } = new();
    public List<InventoryItemInstance> BackpackItems { get; set; } = new();
    public Dictionary<EquipSlot, InventoryItemInstance?> Equipment { get; set; } = new();
}

public enum InventoryContainerKind : byte
{
    Backpack = 1,
    Equipment = 2
}

public struct GridCoord
{
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class InventoryItemInstance
{
    public string InstanceId { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public GridCoord Position { get; set; }
}

public interface IInventoryItemCatalog
{
    bool TryGet(string itemCode, out InventoryItemDefinition definition);
}

public sealed class DictionaryInventoryItemCatalog : IInventoryItemCatalog
{
    private readonly Dictionary<string, InventoryItemDefinition> _byCode;

    public DictionaryInventoryItemCatalog(Dictionary<string, InventoryItemDefinition> byCode)
    {
        _byCode = byCode;
    }

    public bool TryGet(string itemCode, out InventoryItemDefinition definition)
    {
        return _byCode.TryGetValue(itemCode, out definition!);
    }
}

public sealed class InventoryMoveRequest
{
    public InventoryContainerKind FromContainer { get; set; } = InventoryContainerKind.Backpack;
    public InventoryContainerKind ToContainer { get; set; } = InventoryContainerKind.Backpack;
    public GridCoord? FromBackpackCell { get; set; }
    public GridCoord? ToBackpackCell { get; set; }
    public EquipSlot? FromEquipSlot { get; set; }
    public EquipSlot? ToEquipSlot { get; set; }
    public int Quantity { get; set; }
}

public sealed class InventoryDropRequest
{
    public int BackpackIndex { get; set; } = -1;
    public GridCoord? BackpackCell { get; set; }
    public int Quantity { get; set; }
}

public sealed class BackpackToEquipRequest
{
    public int BackpackIndex { get; set; } = -1;
    public GridCoord? BackpackCell { get; set; }
    public EquipSlot TargetSlot { get; set; }
}

public sealed class EquipToBackpackRequest
{
    public EquipSlot SourceSlot { get; set; }
    public int? PreferredBackpackIndex { get; set; }
    public GridCoord? PreferredBackpackCell { get; set; }
}

public readonly struct InventoryOpResult
{
    public InventoryOpResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public bool Success { get; }
    public string Message { get; }

    public static InventoryOpResult Ok() => new(true, string.Empty);
    public static InventoryOpResult Fail(string message) => new(false, message);
}
