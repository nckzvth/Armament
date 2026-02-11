#nullable enable
using System;
using System.Collections.Generic;
using Armament.SharedSim.Sim;
using UnityEngine;

namespace Armament.Client.Networking;

public static class CharacterSlotStorage
{
    private const string PrefSlotCharNamePrefix = "armament.menu.slot.character_name";
    private const string PrefSlotBaseClassPrefix = "armament.menu.slot.base_class";
    private const string PrefSlotSpecPrefix = "armament.menu.slot.spec";

    public static bool TryLoadSlot(string accountSubject, int slot, out string name, out string baseClassId, out string specId)
    {
        var charKey = BuildKey(PrefSlotCharNamePrefix, accountSubject, slot);
        var classKey = BuildKey(PrefSlotBaseClassPrefix, accountSubject, slot);
        var specKey = BuildKey(PrefSlotSpecPrefix, accountSubject, slot);

        name = PlayerPrefs.GetString(charKey, string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            baseClassId = "bastion";
            specId = "spec.bastion.bulwark";
            return false;
        }

        baseClassId = PlayerPrefs.GetString(classKey, "bastion");
        specId = PlayerPrefs.GetString(specKey, string.Empty);
        baseClassId = ClassSpecCatalog.NormalizeBaseClass(baseClassId);
        specId = ClassSpecCatalog.NormalizeSpecForClass(baseClassId, specId);
        return true;
    }

    public static void SaveSlot(string accountSubject, int slot, string name, string baseClassId, string specId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var normalizedClass = ClassSpecCatalog.NormalizeBaseClass(baseClassId);
        var normalizedSpec = ClassSpecCatalog.NormalizeSpecForClass(normalizedClass, specId);

        PlayerPrefs.SetString(BuildKey(PrefSlotCharNamePrefix, accountSubject, slot), name.Trim());
        PlayerPrefs.SetString(BuildKey(PrefSlotBaseClassPrefix, accountSubject, slot), normalizedClass);
        PlayerPrefs.SetString(BuildKey(PrefSlotSpecPrefix, accountSubject, slot), normalizedSpec);
    }

    public static void DeleteSlotAndCompact(string accountSubject, int deletedSlot, int maxSlots)
    {
        for (var i = deletedSlot; i < maxSlots - 1; i++)
        {
            if (!TryLoadSlot(accountSubject, i + 1, out var nextName, out var nextClass, out var nextSpec))
            {
                DeleteSlot(accountSubject, i);
                continue;
            }

            SaveSlot(accountSubject, i, nextName, nextClass, nextSpec);
        }

        DeleteSlot(accountSubject, maxSlots - 1);
    }

    public static void DeleteSlot(string accountSubject, int slot)
    {
        PlayerPrefs.DeleteKey(BuildKey(PrefSlotCharNamePrefix, accountSubject, slot));
        PlayerPrefs.DeleteKey(BuildKey(PrefSlotBaseClassPrefix, accountSubject, slot));
        PlayerPrefs.DeleteKey(BuildKey(PrefSlotSpecPrefix, accountSubject, slot));
    }

    public static bool IsSlotEmpty(string accountSubject, int slot)
    {
        var key = BuildKey(PrefSlotCharNamePrefix, accountSubject, slot);
        var name = PlayerPrefs.GetString(key, string.Empty);
        return string.IsNullOrWhiteSpace(name);
    }

    public static List<int> GetFilledSlots(string accountSubject, int maxSlots)
    {
        var result = new List<int>(maxSlots);
        for (var i = 0; i < maxSlots; i++)
        {
            if (!IsSlotEmpty(accountSubject, i))
            {
                result.Add(i);
            }
        }

        return result;
    }

    public static int GetNextEmptySlot(string accountSubject, int maxSlots)
    {
        for (var i = 0; i < maxSlots; i++)
        {
            if (IsSlotEmpty(accountSubject, i))
            {
                return i;
            }
        }

        return -1;
    }

    public static string BuildKey(string prefix, string accountSubject, int slot)
    {
        var normalized = NormalizeSubject(accountSubject);
        return $"{prefix}.{normalized}.slot_{slot}";
    }

    public static string NormalizeSubject(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "local_guest";
        }

        return input.Trim().ToLowerInvariant()
            .Replace(":", "_")
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace(" ", "_");
    }
}
