#nullable enable
using System;
using System.Text.Json;

namespace Armament.SharedSim.Inventory;

public static class InventoryJsonCodec
{
    public static string Serialize(InventorySnapshot snapshot)
    {
        Normalize(snapshot);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public static InventorySnapshot DeserializeOrDefault(string? json, int backpackWidth = 10, int backpackHeight = 8)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<InventorySnapshot>(json, JsonOptions);
                if (parsed is not null)
                {
                    if (parsed.BackpackWidth <= 0) parsed.BackpackWidth = backpackWidth;
                    if (parsed.BackpackHeight <= 0) parsed.BackpackHeight = backpackHeight;
                    Normalize(parsed);
                    return parsed;
                }
            }
            catch
            {
            }
        }

        var created = new InventorySnapshot
        {
            BackpackWidth = backpackWidth,
            BackpackHeight = backpackHeight
        };
        Normalize(created);
        return created;
    }

    private static void Normalize(InventorySnapshot snapshot)
    {
        InventoryRules.EnsureBackpackSize(snapshot);
        for (var i = 0; i < snapshot.BackpackItems.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.BackpackItems[i].InstanceId))
            {
                continue;
            }

            snapshot.BackpackItems[i].InstanceId = Guid.NewGuid().ToString("N");
        }

        foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
        {
            if (!snapshot.Equipment.ContainsKey(slot))
            {
                snapshot.Equipment[slot] = null;
            }
            else if (snapshot.Equipment[slot] is InventoryItemInstance equipped &&
                     string.IsNullOrWhiteSpace(equipped.InstanceId))
            {
                equipped.InstanceId = Guid.NewGuid().ToString("N");
                snapshot.Equipment[slot] = equipped;
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
