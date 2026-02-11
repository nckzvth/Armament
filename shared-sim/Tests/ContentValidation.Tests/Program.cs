using System.Text.Json;
using Armament.SharedSim.Sim;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
var contentRoot = Path.Combine(repoRoot, "content");

if (!Directory.Exists(contentRoot))
{
    Console.Error.WriteLine($"Content directory not found: {contentRoot}");
    return 1;
}

var failures = new List<string>();

var specs = LoadById<SimSpecContent>(Path.Combine(contentRoot, "specs"), failures, x => x.Id);
var abilities = LoadById<SimAbilityContent>(Path.Combine(contentRoot, "abilities"), failures, x => x.Id);
var statuses = LoadById<StatusDefinition>(Path.Combine(contentRoot, "status"), failures, x => x.Id);
var zones = LoadById<ZoneDefinition>(Path.Combine(contentRoot, "zones"), failures, x => x.Id);
var links = LoadById<LinkDefinition>(Path.Combine(contentRoot, "links"), failures, x => x.Id);
var projectiles = LoadById<ProjectileDefinition>(Path.Combine(contentRoot, "projectiles"), failures, x => x.Id);
var traces = LoadById<TraceDefinition>(Path.Combine(contentRoot, "traces"), failures, x => x.Id);

ValidateSpecCoverage(specs, abilities, statuses, failures);
ValidateAbilities(abilities, statuses, zones, links, projectiles, traces, failures);
ValidateProfileCompilation(specs, abilities, failures);

if (failures.Count > 0)
{
    Console.Error.WriteLine("Class content validation failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($" - {failure}");
    }

    return 1;
}

Console.WriteLine("Class content validation passed.");
return 0;

static Dictionary<string, T> LoadById<T>(string directory, List<string> failures, Func<T, string> idSelector)
{
    var byId = new Dictionary<string, T>(StringComparer.Ordinal);
    if (!Directory.Exists(directory))
    {
        return byId;
    }

    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
    {
        try
        {
            var json = File.ReadAllText(file);
            var parsed = JsonSerializer.Deserialize<T>(json, options);
            if (parsed is null)
            {
                failures.Add($"{file}: failed to parse JSON payload");
                continue;
            }

            var id = idSelector(parsed);
            if (string.IsNullOrWhiteSpace(id))
            {
                failures.Add($"{file}: missing id");
                continue;
            }

            if (!byId.TryAdd(id, parsed))
            {
                failures.Add($"{file}: duplicate id '{id}'");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"{file}: {ex.GetType().Name} - {ex.Message}");
        }
    }

    return byId;
}

static void ValidateSpecCoverage(
    IReadOnlyDictionary<string, SimSpecContent> specs,
    IReadOnlyDictionary<string, SimAbilityContent> abilities,
    IReadOnlyDictionary<string, StatusDefinition> statuses,
    List<string> failures)
{
    if (specs.Count == 0)
    {
        failures.Add("No spec definitions found in content/specs.");
        return;
    }

    var requiredSlots = new[] { "lmb", "rmb", "shift", "e", "r", "q", "t", "1", "2", "3", "4" };

    foreach (var spec in specs.Values)
    {
        if (string.IsNullOrWhiteSpace(spec.CanonicalStatusId) || !statuses.ContainsKey(spec.CanonicalStatusId))
        {
            failures.Add($"Spec '{spec.Id}' references missing canonical status '{spec.CanonicalStatusId}'.");
        }

        foreach (var slot in requiredSlots)
        {
            if (!spec.Slots.TryGetValue(slot, out var abilityId) || string.IsNullOrWhiteSpace(abilityId))
            {
                failures.Add($"Spec '{spec.Id}' missing slot mapping for '{slot}'.");
                continue;
            }

            if (!abilities.TryGetValue(abilityId, out var ability))
            {
                failures.Add($"Spec '{spec.Id}' slot '{slot}' references missing ability '{abilityId}'.");
                continue;
            }

            if (!string.Equals(ability.Slot, slot, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"Ability '{ability.Id}' slot '{ability.Slot}' does not match spec '{spec.Id}' mapping slot '{slot}'.");
            }

            if (!string.Equals(ability.SpecId, spec.Id, StringComparison.Ordinal))
            {
                failures.Add($"Ability '{ability.Id}' declares spec '{ability.SpecId}' but is mapped by '{spec.Id}'.");
            }
        }
    }
}

static void ValidateAbilities(
    IReadOnlyDictionary<string, SimAbilityContent> abilities,
    IReadOnlyDictionary<string, StatusDefinition> statuses,
    IReadOnlyDictionary<string, ZoneDefinition> zones,
    IReadOnlyDictionary<string, LinkDefinition> links,
    IReadOnlyDictionary<string, ProjectileDefinition> projectiles,
    IReadOnlyDictionary<string, TraceDefinition> traces,
    List<string> failures)
{
    var allowedInputBehaviors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "tap",
        "hold_repeat",
        "hold_release_charge"
    };

    var allowedTargetingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "self",
        "unit",
        "point",
        "aim",
        "cone",
        "line",
        "circle",
        "nearest_in_range"
    };

    var allowedPrimitives = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "StartCooldown",
        "RequireResource",
        "SpendResource",
        "GainResource",
        "RequireStatus",
        "ApplyStatus",
        "ConsumeStatus",
        "Cleanse",
        "DealDamage",
        "Heal",
        "ApplyShield",
        "ApplyDR",
        "AddThreat",
        "Taunt",
        "ApplyCC",
        "ApplyDisplacement",
        "SpawnZone",
        "DespawnZone",
        "CreateLink",
        "BreakLink",
        "FireProjectile",
        "HitscanTrace"
    };

    foreach (var ability in abilities.Values)
    {
        if (!allowedInputBehaviors.Contains(ability.InputBehavior))
        {
            failures.Add($"Ability '{ability.Id}' has unsupported input_behavior '{ability.InputBehavior}'.");
        }

        if (ability.CooldownMs < 0)
        {
            failures.Add($"Ability '{ability.Id}' has negative cooldown_ms.");
        }

        if (ability.Targeting is null)
        {
            failures.Add($"Ability '{ability.Id}' missing targeting block.");
            continue;
        }

        if (!allowedTargetingTypes.Contains(ability.Targeting.Type))
        {
            failures.Add($"Ability '{ability.Id}' has unsupported targeting type '{ability.Targeting.Type}'.");
        }

        if (!ability.Targeting.Type.Equals("self", StringComparison.OrdinalIgnoreCase) && ability.Targeting.RangeM <= 0)
        {
            failures.Add($"Ability '{ability.Id}' requires targeting.range_m > 0 for type '{ability.Targeting.Type}'.");
        }

        ValidateTargetingShape(ability, failures);

        if (ability.Effects.Count == 0)
        {
            failures.Add($"Ability '{ability.Id}' has no effects.");
            continue;
        }

        for (var i = 0; i < ability.Effects.Count; i++)
        {
            var effect = ability.Effects[i];
            if (!allowedPrimitives.Contains(effect.Primitive))
            {
                failures.Add($"Ability '{ability.Id}' effect[{i}] has unsupported primitive '{effect.Primitive}'.");
            }

            ValidateReference(effect.StatusId, statuses, failures, ability.Id, i, "status_id");
            ValidateReference(effect.ZoneDefId, zones, failures, ability.Id, i, "zone_def_id");
            ValidateReference(effect.LinkDefId, links, failures, ability.Id, i, "link_def_id");
            ValidateReference(effect.ProjectileDefId, projectiles, failures, ability.Id, i, "projectile_def_id");
            ValidateReference(effect.TraceDefId, traces, failures, ability.Id, i, "trace_def_id");

            if (effect.Amount is < 0)
            {
                failures.Add($"Ability '{ability.Id}' effect[{i}] has negative amount.");
            }

            if (effect.CoefficientPermille is < 0)
            {
                failures.Add($"Ability '{ability.Id}' effect[{i}] has negative coefficientPermille.");
            }
        }
    }
}

static void ValidateTargetingShape(SimAbilityContent ability, List<string> failures)
{
    var targeting = ability.Targeting!;
    if (targeting.Type.Equals("cone", StringComparison.OrdinalIgnoreCase))
    {
        // shape-specific validation is intentionally deferred until shape schema is introduced.
    }

    if (targeting.Type.Equals("line", StringComparison.OrdinalIgnoreCase))
    {
        // shape-specific validation is intentionally deferred until shape schema is introduced.
    }

    if (targeting.Type.Equals("circle", StringComparison.OrdinalIgnoreCase))
    {
        // shape-specific validation is intentionally deferred until shape schema is introduced.
    }
}

static void ValidateProfileCompilation(
    IReadOnlyDictionary<string, SimSpecContent> specs,
    IReadOnlyDictionary<string, SimAbilityContent> abilities,
    List<string> failures)
{
    foreach (var spec in specs.Values)
    {
        if (!AbilityProfileCompiler.TryCompile(spec, abilities, OverworldSimRules.Default, out var profile, out var error))
        {
            failures.Add($"Spec '{spec.Id}' failed runtime profile compilation: {error}");
            continue;
        }

        if (profile.AbilitiesByFlag.Count == 0)
        {
            failures.Add($"Spec '{spec.Id}' compiled to empty ability profile.");
        }
    }
}

static void ValidateReference<T>(
    string? referenceId,
    IReadOnlyDictionary<string, T> byId,
    List<string> failures,
    string abilityId,
    int effectIndex,
    string field)
{
    if (string.IsNullOrWhiteSpace(referenceId))
    {
        return;
    }

    if (!byId.ContainsKey(referenceId))
    {
        failures.Add($"Ability '{abilityId}' effect[{effectIndex}] references missing {field} '{referenceId}'.");
    }
}

public sealed class StatusDefinition
{
    public string Id { get; set; } = string.Empty;
    public int StackCap { get; set; } = 1;
    public string StackAddRule { get; set; } = "add";
    public string RefreshRule { get; set; } = "refresh_duration";
    public int DefaultDurationMs { get; set; }
    public List<string> DispelTags { get; set; } = new();
}

public sealed class ZoneDefinition
{
    public string Id { get; set; } = string.Empty;
}

public sealed class LinkDefinition
{
    public string Id { get; set; } = string.Empty;
}

public sealed class ProjectileDefinition
{
    public string Id { get; set; } = string.Empty;
}

public sealed class TraceDefinition
{
    public string Id { get; set; } = string.Empty;
}
