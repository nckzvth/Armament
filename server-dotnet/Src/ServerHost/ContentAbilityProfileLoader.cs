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
            var message = $"precompiled {compiled.Count} profiles; per-character spec resolution enabled; fallback '{fallbackSpecId}'; loaded=[{loadedIds}]";
            return new LoadedAbilityProfiles(compiled, fallbackSpecId, message);
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
