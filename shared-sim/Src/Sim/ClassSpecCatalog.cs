#nullable enable
using System;
using System.Collections.Generic;

namespace Armament.SharedSim.Sim;

public static class ClassSpecCatalog
{
    public static readonly string[] BaseClasses =
    {
        "bastion",
        "exorcist",
        "tidebinder",
        "gunslinger",
        "dreadweaver",
        "arbiter"
    };

    private static readonly Dictionary<string, string[]> SpecsByClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bastion"] = new[] { "spec.bastion.bulwark", "spec.bastion.cataclysm" },
        ["exorcist"] = new[] { "spec.exorcist.warden", "spec.exorcist.inquisitor" },
        ["tidebinder"] = new[] { "spec.tidebinder.tidecaller", "spec.tidebinder.tempest" },
        ["gunslinger"] = new[] { "spec.gunslinger.akimbo", "spec.gunslinger.deadeye" },
        ["dreadweaver"] = new[] { "spec.dreadweaver.menace", "spec.dreadweaver.deceiver" },
        ["arbiter"] = new[] { "spec.arbiter.aegis", "spec.arbiter.edict" }
    };

    public static string NormalizeBaseClass(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (SpecsByClass.ContainsKey(normalized))
        {
            return normalized;
        }

        return "bastion";
    }

    public static string NormalizeSpecForClass(string baseClassId, string? requestedSpecId)
    {
        var normalizedClass = NormalizeBaseClass(baseClassId);
        var specs = SpecsByClass[normalizedClass];
        if (!string.IsNullOrWhiteSpace(requestedSpecId))
        {
            var normalizedRequested = NormalizeLegacySpecId(requestedSpecId.Trim());
            for (var i = 0; i < specs.Length; i++)
            {
                if (string.Equals(specs[i], normalizedRequested, StringComparison.OrdinalIgnoreCase))
                {
                    return specs[i];
                }
            }
        }

        return specs[0];
    }

    public static IReadOnlyList<string> GetSpecsForClass(string baseClassId)
    {
        var normalized = NormalizeBaseClass(baseClassId);
        return SpecsByClass[normalized];
    }

    private static string NormalizeLegacySpecId(string requestedSpecId)
    {
        if (string.Equals(requestedSpecId, "spec.dreadweaver.weaver", StringComparison.OrdinalIgnoreCase))
        {
            return "spec.dreadweaver.deceiver";
        }

        return requestedSpecId;
    }
}
