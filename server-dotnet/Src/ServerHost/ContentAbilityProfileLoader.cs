using System.Text.Json;
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
            var contentRoot = Path.Combine(repoRoot, "content");
            var specsDir = Path.Combine(contentRoot, "specs");
            var abilitiesDir = Path.Combine(contentRoot, "abilities");
            if (!Directory.Exists(specsDir) || !Directory.Exists(abilitiesDir))
            {
                return BuildBuiltinOnly("content directories missing; using builtin profile only");
            }

            var abilities = LoadAbilities(abilitiesDir);
            var specs = LoadSpecs(specsDir);
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
            foreach (var spec in specs)
            {
                if (!AbilityProfileCompiler.TryCompile(spec, abilities, rules, out var profile, out _))
                {
                    continue;
                }

                compiled[spec.Id] = profile;
            }

            var requestedSpecId = Environment.GetEnvironmentVariable("ARMAMENT_SPEC_ID") ?? "spec.bastion.bulwark";
            var fallbackSpecId = compiled.ContainsKey(requestedSpecId)
                ? requestedSpecId
                : (compiled.Keys.FirstOrDefault(id => id != SimAbilityProfiles.BuiltinV1.Id) ?? SimAbilityProfiles.BuiltinV1.Id);

            var message = $"precompiled {compiled.Count} profiles; per-character spec resolution enabled; fallback '{fallbackSpecId}'";
            return new LoadedAbilityProfiles(compiled, fallbackSpecId, message);
        }
        catch (Exception ex)
        {
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
            SimAbilityProfiles.BuiltinV1.Id,
            message);
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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }
}
