#nullable enable
using System;
using System.Collections.Concurrent;
using Armament.SharedSim.Inventory;

namespace Armament.GameServer.Inventory;

// Server-authoritative inventory/equipment operations.
public sealed class AuthoritativeInventoryService
{
    private readonly IInventoryItemCatalog _catalog;
    private readonly ConcurrentDictionary<Guid, InventorySnapshot> _snapshots = new();

    public AuthoritativeInventoryService(IInventoryItemCatalog catalog)
    {
        _catalog = catalog;
    }

    public InventorySnapshot GetOrCreate(Guid characterId)
    {
        return _snapshots.GetOrAdd(characterId, static _ =>
        {
            var snapshot = new InventorySnapshot();
            InventoryRules.EnsureBackpackSize(snapshot);
            return snapshot;
        });
    }

    public void UpsertFromJson(Guid characterId, string? inventoryJson)
    {
        _snapshots[characterId] = InventoryJsonCodec.DeserializeOrDefault(inventoryJson);
    }

    public string ExportJson(Guid characterId)
    {
        return InventoryJsonCodec.Serialize(GetOrCreate(characterId));
    }

    public InventoryOpResult GrantItem(Guid characterId, string itemCode, int quantity)
    {
        var snapshot = GetOrCreate(characterId);
        return InventoryOps.AddToBackpack(snapshot, _catalog, new InventoryItemStack
        {
            ItemCode = itemCode,
            Quantity = quantity
        });
    }

    public InventoryOpResult ApplyMove(Guid characterId, InventoryMoveRequest request)
    {
        var snapshot = GetOrCreate(characterId);
        return InventoryOps.MoveBackpack(snapshot, _catalog, request);
    }

    public InventoryOpResult ApplyDrop(Guid characterId, InventoryDropRequest request)
    {
        var snapshot = GetOrCreate(characterId);
        return InventoryOps.DropFromBackpack(snapshot, _catalog, request);
    }

    public InventoryOpResult BackpackToEquip(Guid characterId, BackpackToEquipRequest request)
    {
        var snapshot = GetOrCreate(characterId);
        return InventoryOps.BackpackToEquip(snapshot, _catalog, request);
    }

    public InventoryOpResult EquipToBackpack(Guid characterId, EquipToBackpackRequest request)
    {
        var snapshot = GetOrCreate(characterId);
        return InventoryOps.EquipToBackpack(snapshot, _catalog, request);
    }

    public InventoryOpResult QuickEquip(Guid characterId, int backpackIndex)
    {
        var snapshot = GetOrCreate(characterId);
        return InventoryOps.QuickEquip(snapshot, _catalog, backpackIndex);
    }

    public InventoryOpResult QuickUnequip(Guid characterId, EquipSlot slot)
    {
        var snapshot = GetOrCreate(characterId);
        return InventoryOps.QuickUnequip(snapshot, _catalog, slot);
    }
}
