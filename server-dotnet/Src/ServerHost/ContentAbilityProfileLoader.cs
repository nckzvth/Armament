using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using Armament.GameServer;
using Armament.SharedSim.Sim;

namespace Armament.ServerHost;

public static class ContentAbilityProfileLoader
{
    public static LoadedAbilityProfiles LoadOrFallback(string repoRoot, int simulationHz)
    {
        try
        {
            var contentRoot = ResolveContentRoot(repoRoot);
            if (contentRoot is null)
            {
                return BuildBuiltinOnly("content root not found; using builtin profile only");
            }

            var specsDir = Path.Combine(contentRoot, "specs");
            var abilitiesDir = Path.Combine(contentRoot, "abilities");
            if (!Directory.Exists(specsDir) || !Directory.Exists(abilitiesDir))
            {
                return BuildBuiltinOnly("content directories missing; using builtin profile only");
            }

            var abilities = LoadAbilities(abilitiesDir);
            var specs = LoadSpecs(specsDir);
            var zones = LoadZones(Path.Combine(contentRoot, "zones"), rulesHz: simulationHz);
            var links = LoadLinks(Path.Combine(contentRoot, "links"), rulesHz: simulationHz);
            if (specs.Count == 0)
            {
                return BuildBuiltinOnly("no specs found in content/specs; using builtin profile only");
            }

            var rules = OverworldSimRules.Default;
            rules.SimulationHz = simulationHz;

            var compiled = new Dictionary<string, SimAbilityProfile>(StringComparer.Ordinal)
            {
                [SimAbilityProfiles.BuiltinV1.Id] = SimAbilityProfiles.BuiltinV1
            };
            var compileFailures = new List<string>();
            foreach (var spec in specs)
            {
                if (!AbilityProfileCompiler.TryCompile(spec, abilities, rules, out var profile, out var error))
                {
                    compileFailures.Add($"{spec.Id}: {error}");
                    continue;
                }

                compiled[spec.Id] = profile;
            }

            if (compileFailures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Spec compilation failed for {compileFailures.Count} content spec(s): {string.Join(" | ", compileFailures)}");
            }

            var requestedSpecId = Environment.GetEnvironmentVariable("ARMAMENT_SPEC_ID") ?? "spec.bastion.bulwark";
            var fallbackSpecId = compiled.ContainsKey(requestedSpecId)
                ? requestedSpecId
                : (compiled.Keys.FirstOrDefault(id => id != SimAbilityProfiles.BuiltinV1.Id) ?? SimAbilityProfiles.BuiltinV1.Id);

            var requiredSpecId = Environment.GetEnvironmentVariable("ARMAMENT_REQUIRED_SPEC_ID");
            if (!string.IsNullOrWhiteSpace(requiredSpecId))
            {
                var required = requiredSpecId.Trim();
                if (!compiled.ContainsKey(required))
                {
                    var availableIds = string.Join(", ", compiled.Keys.OrderBy(x => x, StringComparer.Ordinal));
                    throw new InvalidOperationException(
                        $"required spec '{required}' was not loaded; available=[{availableIds}]");
                }
            }

            var loadedIds = string.Join(", ", compiled.Keys.OrderBy(x => x, StringComparer.Ordinal));
            var message = $"precompiled {compiled.Count} profiles; zoneDefs={zones.Count}; linkDefs={links.Count}; per-character spec resolution enabled; fallback '{fallbackSpecId}'; loaded=[{loadedIds}]";
            return new LoadedAbilityProfiles(compiled, zones, links, fallbackSpecId, message);
        }
        catch (Exception ex)
        {
            if (ex is InvalidOperationException)
            {
                throw;
            }

            return BuildBuiltinOnly($"content profile load error ({ex.Message}); using builtin profile only");
        }
    }

    private static LoadedAbilityProfiles BuildBuiltinOnly(string message)
    {
        return new LoadedAbilityProfiles(
            new Dictionary<string, SimAbilityProfile>(StringComparer.Ordinal)
            {
                [SimAbilityProfiles.BuiltinV1.Id] = SimAbilityProfiles.BuiltinV1
            },
            BuildDefaultZoneDefinitions(),
            BuildDefaultLinkDefinitions(),
            SimAbilityProfiles.BuiltinV1.Id,
            message);
    }

    private static string? ResolveContentRoot(string repoRoot)
    {
        static bool IsValidContentRoot(string path)
        {
            return Directory.Exists(Path.Combine(path, "specs")) &&
                   Directory.Exists(Path.Combine(path, "abilities"));
        }

        var direct = Path.Combine(repoRoot, "content");
        if (IsValidContentRoot(direct))
        {
            return direct;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 10 && dir is not null; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "content");
            if (IsValidContentRoot(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static List<SimSpecContent> LoadSpecs(string specsDir)
    {
        var options = CreateJsonOptions();
        var specs = new List<SimSpecContent>();
        foreach (var file in Directory.EnumerateFiles(specsDir, "*.json", SearchOption.AllDirectories))
        {
            var payload = File.ReadAllText(file);
            var parsed = JsonSerializer.Deserialize<SimSpecContent>(payload, options);
            if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Id))
            {
                specs.Add(parsed);
            }
        }

        return specs;
    }

    private static Dictionary<string, SimAbilityContent> LoadAbilities(string abilitiesDir)
    {
        var options = CreateJsonOptions();
        var byId = new Dictionary<string, SimAbilityContent>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(abilitiesDir, "*.json", SearchOption.AllDirectories))
        {
            var payload = File.ReadAllText(file);
            var parsed = JsonSerializer.Deserialize<SimAbilityContent>(payload, options);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Id))
            {
                continue;
            }

            byId[parsed.Id] = parsed;
        }

        return byId;
    }

    private static Dictionary<string, SimZoneDefinition> LoadZones(string zonesDir, int rulesHz)
    {
        var byId = new Dictionary<string, SimZoneDefinition>(StringComparer.Ordinal);
        foreach (var zone in SimZoneLinkDefaults.Zones)
        {
            byId[zone.Id] = zone;
        }

        if (!Directory.Exists(zonesDir))
        {
            return byId;
        }

        var options = CreateJsonOptions();
        foreach (var file in Directory.EnumerateFiles(zonesDir, "*.json", SearchOption.AllDirectories))
        {
            var payload = File.ReadAllText(file);
            var parsed = JsonSerializer.Deserialize<ZoneDefinitionContent>(payload, options);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Id))
            {
                continue;
            }

            var tick = MillisToTicks(parsed.TickIntervalMs, rulesHz, fallback: 12);
            byId[parsed.Id] = new SimZoneDefinition
            {
                Id = parsed.Id,
                RadiusMilli = parsed.RadiusMilli > 0 ? parsed.RadiusMilli : 1500,
                DurationTicks = MillisToTicks(parsed.DurationMs, rulesHz, fallback: 300),
                TickIntervalTicks = tick,
                DamagePerPulse = Math.Max(0, parsed.DamagePerPulse),
                HealPerPulse = Math.Max(0, parsed.HealPerPulse),
                StatusId = parsed.StatusId,
                StatusDurationTicks = MillisToTicks(parsed.StatusDurationMs, rulesHz, fallback: tick)
            };
        }

        return byId;
    }

    private static Dictionary<string, SimLinkDefinition> LoadLinks(string linksDir, int rulesHz)
    {
        var byId = new Dictionary<string, SimLinkDefinition>(StringComparer.Ordinal);
        foreach (var link in SimZoneLinkDefaults.Links)
        {
            byId[link.Id] = link;
        }

        if (!Directory.Exists(linksDir))
        {
            return byId;
        }

        var options = CreateJsonOptions();
        foreach (var file in Directory.EnumerateFiles(linksDir, "*.json", SearchOption.AllDirectories))
        {
            var payload = File.ReadAllText(file);
            var parsed = JsonSerializer.Deserialize<LinkDefinitionContent>(payload, options);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Id))
            {
                continue;
            }

            byId[parsed.Id] = new SimLinkDefinition
            {
                Id = parsed.Id,
                DurationTicks = MillisToTicks(parsed.DurationMs, rulesHz, fallback: 300),
                MaxDistanceMilli = parsed.MaxDistanceMilli > 0 ? parsed.MaxDistanceMilli : 6000,
                PullMilliPerTick = Math.Max(0, parsed.PullMilliPerTick),
                DamagePerTick = Math.Max(0, parsed.DamagePerTick),
                MaxActiveLinks = parsed.MaxActiveLinks > 0 ? parsed.MaxActiveLinks : 1
            };
        }

        return byId;
    }

    private static int MillisToTicks(int millis, int simulationHz, int fallback)
    {
        if (millis <= 0 || simulationHz <= 0)
        {
            return fallback;
        }

        return Math.Max(1, (int)Math.Ceiling(millis / (1000m / simulationHz)));
    }

    private static Dictionary<string, SimZoneDefinition> BuildDefaultZoneDefinitions()
    {
        var byId = new Dictionary<string, SimZoneDefinition>(StringComparer.Ordinal);
        foreach (var zone in SimZoneLinkDefaults.Zones)
        {
            byId[zone.Id] = zone;
        }

        return byId;
    }

    private static Dictionary<string, SimLinkDefinition> BuildDefaultLinkDefinitions()
    {
        var byId = new Dictionary<string, SimLinkDefinition>(StringComparer.Ordinal);
        foreach (var link in SimZoneLinkDefaults.Links)
        {
            byId[link.Id] = link;
        }

        return byId;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    private sealed class ZoneDefinitionContent
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("radius_milli")]
        public int RadiusMilli { get; set; }
        [JsonPropertyName("duration_ms")]
        public int DurationMs { get; set; }
        [JsonPropertyName("tick_interval_ms")]
        public int TickIntervalMs { get; set; }
        [JsonPropertyName("damage_per_pulse")]
        public int DamagePerPulse { get; set; }
        [JsonPropertyName("heal_per_pulse")]
        public int HealPerPulse { get; set; }
        [JsonPropertyName("status_id")]
        public string? StatusId { get; set; }
        [JsonPropertyName("status_duration_ms")]
        public int StatusDurationMs { get; set; }
    }

    private sealed class LinkDefinitionContent
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("duration_ms")]
        public int DurationMs { get; set; }
        [JsonPropertyName("max_distance_milli")]
        public int MaxDistanceMilli { get; set; }
        [JsonPropertyName("pull_milli_per_tick")]
        public int PullMilliPerTick { get; set; }
        [JsonPropertyName("damage_per_tick")]
        public int DamagePerTick { get; set; }
        [JsonPropertyName("max_active_links")]
        public int MaxActiveLinks { get; set; }
    }
}
