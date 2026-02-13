using System.Text.Json;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
var worldRoot = Path.Combine(repoRoot, "content", "world");
var act1Root = Path.Combine(worldRoot, "acts", "act1");

if (!Directory.Exists(worldRoot) || !Directory.Exists(act1Root))
{
    Console.Error.WriteLine($"World content directory missing: {worldRoot}");
    return 1;
}

var failures = new List<string>();
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
};

var act = LoadSingle<ActContent>(Path.Combine(act1Root, "act.json"), failures, options);
var zones = LoadById<ZoneContent>(Path.Combine(act1Root, "zones"), failures, x => x.Id, options);
var quests = LoadById<QuestContent>(Path.Combine(act1Root, "quests"), failures, x => x.Id, options);
var encounters = LoadById<EncounterContent>(Path.Combine(act1Root, "encounters"), failures, x => x.Id, options);

var bestiary = LoadArraysById<BestiaryContent>(Path.Combine(worldRoot, "bestiary"), failures, x => x.Id, options);
var npcs = LoadArraysById<NpcContent>(Path.Combine(worldRoot, "npcs"), failures, x => x.Id, options);
var objects = LoadArraysById<ObjectContent>(Path.Combine(worldRoot, "objects"), failures, x => x.Id, options);
var hazards = LoadArraysById<HazardContent>(Path.Combine(worldRoot, "hazards"), failures, x => x.Id, options);
var zoneLayouts = LoadZoneLayouts(act1Root, zones, failures);

ValidateAct(act, zones, quests, failures);
ValidateZones(zones, encounters, zoneLayouts, failures);
ValidateEncounters(encounters, zones, bestiary, objects, hazards, failures);
ValidateQuests(quests, encounters, npcs, objects, failures);
ValidateBestiary(bestiary, failures);
ValidateNpcs(npcs, zones, failures);
ValidateObjects(objects, hazards, failures);
ValidateHazards(hazards, failures);
ValidateZoneLayouts(zoneLayouts, zones, encounters, npcs, objects, hazards, failures);

if (failures.Count > 0)
{
    Console.Error.WriteLine("World content validation failed:");
    foreach (var f in failures)
    {
        Console.Error.WriteLine($" - {f}");
    }

    return 1;
}

Console.WriteLine("World content validation passed.");
return 0;

static T? LoadSingle<T>(string path, List<string> failures, JsonSerializerOptions options)
{
    if (!File.Exists(path))
    {
        failures.Add($"Missing required file: {path}");
        return default;
    }

    try
    {
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options);
    }
    catch (Exception ex)
    {
        failures.Add($"{path}: {ex.GetType().Name} - {ex.Message}");
        return default;
    }
}

static Dictionary<string, T> LoadById<T>(string dir, List<string> failures, Func<T, string> idSelector, JsonSerializerOptions options)
{
    var map = new Dictionary<string, T>(StringComparer.Ordinal);
    if (!Directory.Exists(dir))
    {
        failures.Add($"Missing directory: {dir}");
        return map;
    }

    foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
    {
        try
        {
            var item = JsonSerializer.Deserialize<T>(File.ReadAllText(file), options);
            if (item is null)
            {
                failures.Add($"{file}: failed to parse JSON payload");
                continue;
            }

            var id = idSelector(item);
            if (string.IsNullOrWhiteSpace(id))
            {
                failures.Add($"{file}: missing id");
                continue;
            }

            if (!map.TryAdd(id, item))
            {
                failures.Add($"{file}: duplicate id '{id}'");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"{file}: {ex.GetType().Name} - {ex.Message}");
        }
    }

    return map;
}

static Dictionary<string, T> LoadArraysById<T>(string dir, List<string> failures, Func<T, string> idSelector, JsonSerializerOptions options)
{
    var map = new Dictionary<string, T>(StringComparer.Ordinal);
    if (!Directory.Exists(dir))
    {
        failures.Add($"Missing directory: {dir}");
        return map;
    }

    foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(file), options);
            if (items is null)
            {
                failures.Add($"{file}: failed to parse JSON array payload");
                continue;
            }

            foreach (var item in items)
            {
                var id = idSelector(item);
                if (string.IsNullOrWhiteSpace(id))
                {
                    failures.Add($"{file}: element missing id");
                    continue;
                }

                if (!map.TryAdd(id, item))
                {
                    failures.Add($"{file}: duplicate id '{id}'");
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add($"{file}: {ex.GetType().Name} - {ex.Message}");
        }
    }

    return map;
}

static void ValidateAct(
    ActContent? act,
    IReadOnlyDictionary<string, ZoneContent> zones,
    IReadOnlyDictionary<string, QuestContent> quests,
    List<string> failures)
{
    if (act is null)
    {
        failures.Add("Act payload missing or invalid.");
        return;
    }

    if (string.IsNullOrWhiteSpace(act.Id)) failures.Add("act.json missing id");
    if (string.IsNullOrWhiteSpace(act.StartZoneId)) failures.Add("act.json missing startZoneId");
    else if (!zones.ContainsKey(act.StartZoneId)) failures.Add($"act '{act.Id}' startZoneId '{act.StartZoneId}' missing zone definition");

    if (act.ZoneIds.Count == 0) failures.Add($"act '{act.Id}' must declare at least one zone id");
    for (var i = 0; i < act.ZoneIds.Count; i++)
    {
        var id = act.ZoneIds[i];
        if (!zones.ContainsKey(id)) failures.Add($"act '{act.Id}' references missing zone '{id}'");
    }

    if (act.MainQuestIds.Count == 0) failures.Add($"act '{act.Id}' must declare at least one main quest id");
    for (var i = 0; i < act.MainQuestIds.Count; i++)
    {
        var id = act.MainQuestIds[i];
        if (!quests.ContainsKey(id)) failures.Add($"act '{act.Id}' references missing main quest '{id}'");
    }
}

static void ValidateZones(
    IReadOnlyDictionary<string, ZoneContent> zones,
    IReadOnlyDictionary<string, EncounterContent> encounters,
    IReadOnlyDictionary<string, ZoneLayoutContent> layouts,
    List<string> failures)
{
    if (zones.Count == 0)
    {
        failures.Add("No zones found under content/world/acts/act1/zones.");
        return;
    }

    var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var zone in zones.Values)
    {
        if (string.IsNullOrWhiteSpace(zone.Key)) failures.Add($"zone '{zone.Id}' missing key");
        if (string.IsNullOrWhiteSpace(zone.Name)) failures.Add($"zone '{zone.Id}' missing name");
        if (!string.IsNullOrWhiteSpace(zone.Key) && !seenKeys.Add(zone.Key)) failures.Add($"zone key '{zone.Key}' is duplicated");
        if (zone.EncounterIds.Count == 0) failures.Add($"zone '{zone.Id}' has no encounterIds");

        for (var i = 0; i < zone.EncounterIds.Count; i++)
        {
            var encounterId = zone.EncounterIds[i];
            if (!encounters.ContainsKey(encounterId)) failures.Add($"zone '{zone.Id}' references missing encounter '{encounterId}'");
        }

        if (!string.IsNullOrWhiteSpace(zone.MapFile) && !layouts.ContainsKey(zone.Id))
        {
            failures.Add($"zone '{zone.Id}' mapFile '{zone.MapFile}' failed to load");
        }
    }
}

static Dictionary<string, ZoneLayoutContent> LoadZoneLayouts(
    string actRoot,
    IReadOnlyDictionary<string, ZoneContent> zones,
    List<string> failures)
{
    var layouts = new Dictionary<string, ZoneLayoutContent>(StringComparer.Ordinal);
    foreach (var zone in zones.Values)
    {
        if (string.IsNullOrWhiteSpace(zone.MapFile))
        {
            continue;
        }

        var mapPath = Path.Combine(actRoot, zone.MapFile);
        if (!File.Exists(mapPath))
        {
            failures.Add($"zone '{zone.Id}' map file not found: {zone.MapFile}");
            continue;
        }

        try
        {
            layouts[zone.Id] = ParseZoneLayout(zone.Id, mapPath);
        }
        catch (Exception ex)
        {
            failures.Add($"zone '{zone.Id}' map parse failed ({zone.MapFile}): {ex.Message}");
        }
    }

    return layouts;
}

static ZoneLayoutContent ParseZoneLayout(string zoneId, string mapPath)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(mapPath));
    var root = doc.RootElement;
    var layout = new ZoneLayoutContent { ZoneId = zoneId };

    if (!root.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array)
    {
        return layout;
    }

    for (var i = 0; i < layers.GetArrayLength(); i++)
    {
        var layer = layers[i];
        if (!layer.TryGetProperty("type", out var type) || !string.Equals(type.GetString(), "objectgroup", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!layer.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array)
        {
            continue;
        }

        for (var j = 0; j < objects.GetArrayLength(); j++)
        {
            var obj = objects[j];
            var objType = obj.TryGetProperty("type", out var objTypeValue) ? (objTypeValue.GetString() ?? string.Empty) : string.Empty;
            var normalizedType = objType.Trim().ToLowerInvariant();
            if (normalizedType == "campaign_npc")
            {
                var npcId = ReadTiledStringProperty(obj, "npcId");
                if (!string.IsNullOrWhiteSpace(npcId))
                {
                    layout.NpcIds.Add(npcId);
                }

                continue;
            }

            var encounterId = ReadTiledStringProperty(obj, "encounterId");
            if (string.IsNullOrWhiteSpace(encounterId))
            {
                continue;
            }

            if (!layout.Encounters.TryGetValue(encounterId, out var encounter))
            {
                encounter = new EncounterLayoutContent { EncounterId = encounterId };
                layout.Encounters[encounterId] = encounter;
            }

            switch (normalizedType)
            {
                case "campaign_object":
                {
                    var objectDefId = ReadTiledStringProperty(obj, "objectDefId");
                    if (!string.IsNullOrWhiteSpace(objectDefId))
                    {
                        encounter.ObjectDefIds.Add(objectDefId);
                    }

                    break;
                }
                case "campaign_hazard":
                {
                    var hazardId = ReadTiledStringProperty(obj, "hazardId");
                    if (!string.IsNullOrWhiteSpace(hazardId))
                    {
                        encounter.HazardIds.Add(hazardId);
                    }

                    break;
                }
            }
        }
    }

    return layout;
}

static string? ReadTiledStringProperty(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
    {
        return null;
    }

    for (var i = 0; i < properties.GetArrayLength(); i++)
    {
        var prop = properties[i];
        if (!prop.TryGetProperty("name", out var name) || !string.Equals(name.GetString(), propertyName, StringComparison.Ordinal))
        {
            continue;
        }

        if (prop.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
    }

    return null;
}

static void ValidateZoneLayouts(
    IReadOnlyDictionary<string, ZoneLayoutContent> layouts,
    IReadOnlyDictionary<string, ZoneContent> zones,
    IReadOnlyDictionary<string, EncounterContent> encounters,
    IReadOnlyDictionary<string, NpcContent> npcs,
    IReadOnlyDictionary<string, ObjectContent> objects,
    IReadOnlyDictionary<string, HazardContent> hazards,
    List<string> failures)
{
    foreach (var layout in layouts.Values)
    {
        if (!zones.TryGetValue(layout.ZoneId, out var zone))
        {
            failures.Add($"layout references unknown zone '{layout.ZoneId}'");
            continue;
        }

        var zoneEncounterIds = new HashSet<string>(zone.EncounterIds, StringComparer.Ordinal);
        for (var i = 0; i < zone.EncounterIds.Count; i++)
        {
            var encounterId = zone.EncounterIds[i];
            if (!encounters.TryGetValue(encounterId, out var encounter))
            {
                continue;
            }

            if (encounter.ObjectiveObjectIds.Count == 0 && encounter.HazardIds.Count == 0)
            {
                continue;
            }

            if (!layout.Encounters.ContainsKey(encounterId))
            {
                failures.Add($"layout zone '{layout.ZoneId}' missing encounter entry '{encounterId}'");
            }
        }

        foreach (var encounterLayout in layout.Encounters.Values)
        {
            if (!encounters.TryGetValue(encounterLayout.EncounterId, out var encounter))
            {
                failures.Add($"layout zone '{layout.ZoneId}' references missing encounter '{encounterLayout.EncounterId}'");
                continue;
            }

            if (!zoneEncounterIds.Contains(encounterLayout.EncounterId))
            {
                failures.Add($"layout encounter '{encounterLayout.EncounterId}' not listed under zone '{layout.ZoneId}'");
            }

            var allowedObjects = new HashSet<string>(encounter.ObjectiveObjectIds, StringComparer.Ordinal);
            var placedObjects = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < encounterLayout.ObjectDefIds.Count; i++)
            {
                var objectDefId = encounterLayout.ObjectDefIds[i];
                if (!objects.ContainsKey(objectDefId))
                {
                    failures.Add($"layout encounter '{encounterLayout.EncounterId}' unknown objectDefId '{objectDefId}'");
                }
                else if (!allowedObjects.Contains(objectDefId))
                {
                    failures.Add($"layout encounter '{encounterLayout.EncounterId}' objectDefId '{objectDefId}' is not in objectiveObjectIds");
                }
                else
                {
                    placedObjects.Add(objectDefId);
                }
            }

            var allowedHazards = new HashSet<string>(encounter.HazardIds, StringComparer.Ordinal);
            var placedHazards = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < encounterLayout.HazardIds.Count; i++)
            {
                var hazardId = encounterLayout.HazardIds[i];
                if (!hazards.ContainsKey(hazardId))
                {
                    failures.Add($"layout encounter '{encounterLayout.EncounterId}' unknown hazardId '{hazardId}'");
                }
                else if (!allowedHazards.Contains(hazardId))
                {
                    failures.Add($"layout encounter '{encounterLayout.EncounterId}' hazardId '{hazardId}' is not in hazardIds");
                }
                else
                {
                    placedHazards.Add(hazardId);
                }
            }

            foreach (var objectDefId in encounter.ObjectiveObjectIds)
            {
                if (!placedObjects.Contains(objectDefId))
                {
                    failures.Add($"layout encounter '{encounterLayout.EncounterId}' missing placement for objective object '{objectDefId}'");
                }
            }

            foreach (var hazardId in encounter.HazardIds)
            {
                if (!placedHazards.Contains(hazardId))
                {
                    failures.Add($"layout encounter '{encounterLayout.EncounterId}' missing placement for hazard '{hazardId}'");
                }
            }
        }

        var placedNpcIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < layout.NpcIds.Count; i++)
        {
            var npcId = layout.NpcIds[i];
            if (!npcs.TryGetValue(npcId, out var npc))
            {
                failures.Add($"layout zone '{layout.ZoneId}' unknown npcId '{npcId}'");
                continue;
            }

            if (!string.Equals(npc.ZoneId, layout.ZoneId, StringComparison.Ordinal))
            {
                failures.Add($"layout zone '{layout.ZoneId}' npc '{npcId}' belongs to zone '{npc.ZoneId}'");
            }

            if (!placedNpcIds.Add(npcId))
            {
                failures.Add($"layout zone '{layout.ZoneId}' duplicate npc placement '{npcId}'");
            }
        }

        foreach (var npc in npcs.Values)
        {
            if (!string.Equals(npc.ZoneId, layout.ZoneId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!placedNpcIds.Contains(npc.Id))
            {
                failures.Add($"layout zone '{layout.ZoneId}' missing placement for npc '{npc.Id}'");
            }
        }
    }
}

static void ValidateEncounters(
    IReadOnlyDictionary<string, EncounterContent> encounters,
    IReadOnlyDictionary<string, ZoneContent> zones,
    IReadOnlyDictionary<string, BestiaryContent> bestiary,
    IReadOnlyDictionary<string, ObjectContent> objects,
    IReadOnlyDictionary<string, HazardContent> hazards,
    List<string> failures)
{
    if (encounters.Count == 0)
    {
        failures.Add("No encounters found under content/world/acts/act1/encounters.");
        return;
    }

    var allowedCompletionKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "EnemyKilled",
        "ObjectDestroyed",
        "ObjectiveCompleted",
        "TokenCollected",
        "PlayerEnteredRegion"
    };

    foreach (var enc in encounters.Values)
    {
        if (string.IsNullOrWhiteSpace(enc.ZoneId)) failures.Add($"encounter '{enc.Id}' missing zoneId");
        else if (!zones.ContainsKey(enc.ZoneId)) failures.Add($"encounter '{enc.Id}' references missing zone '{enc.ZoneId}'");

        if (enc.EnemyIds.Count == 0) failures.Add($"encounter '{enc.Id}' has no enemyIds");

        for (var i = 0; i < enc.EnemyIds.Count; i++)
        {
            var id = enc.EnemyIds[i];
            if (!bestiary.ContainsKey(id)) failures.Add($"encounter '{enc.Id}' references missing enemy '{id}'");
        }

        for (var i = 0; i < enc.ObjectiveObjectIds.Count; i++)
        {
            var id = enc.ObjectiveObjectIds[i];
            if (!objects.ContainsKey(id)) failures.Add($"encounter '{enc.Id}' references missing object '{id}'");
        }

        for (var i = 0; i < enc.HazardIds.Count; i++)
        {
            var id = enc.HazardIds[i];
            if (!hazards.ContainsKey(id)) failures.Add($"encounter '{enc.Id}' references missing hazard '{id}'");
        }

        if (!string.IsNullOrWhiteSpace(enc.CompletionEventKind) && !allowedCompletionKinds.Contains(enc.CompletionEventKind))
        {
            failures.Add($"encounter '{enc.Id}' has unsupported completionEventKind '{enc.CompletionEventKind}'");
        }

        if (!string.IsNullOrWhiteSpace(enc.CompletionEventKind) && enc.CompletionCount <= 0)
        {
            failures.Add($"encounter '{enc.Id}' has completionEventKind but invalid completionCount '{enc.CompletionCount}'");
        }

        if (!string.IsNullOrWhiteSpace(enc.RewardItemCode) && enc.RewardItemQuantity <= 0)
        {
            failures.Add($"encounter '{enc.Id}' has rewardItemCode but invalid rewardItemQuantity '{enc.RewardItemQuantity}'");
        }
    }
}

static void ValidateQuests(
    IReadOnlyDictionary<string, QuestContent> quests,
    IReadOnlyDictionary<string, EncounterContent> encounters,
    IReadOnlyDictionary<string, NpcContent> npcs,
    IReadOnlyDictionary<string, ObjectContent> objects,
    List<string> failures)
{
    if (quests.Count == 0)
    {
        failures.Add("No quests found under content/world/acts/act1/quests.");
        return;
    }

    var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "TalkToNpc",
        "CompleteEncounter",
        "DestroyObjectType",
        "KillEnemyType",
        "KillEnemyTag",
        "CollectToken",
        "EnterRegion",
        "ReachWaypoint"
    };

    foreach (var quest in quests.Values)
    {
        if (string.IsNullOrWhiteSpace(quest.Title)) failures.Add($"quest '{quest.Id}' missing title");

        for (var i = 0; i < quest.PrerequisiteQuestIds.Count; i++)
        {
            var dep = quest.PrerequisiteQuestIds[i];
            if (!quests.ContainsKey(dep)) failures.Add($"quest '{quest.Id}' prerequisite '{dep}' not found");
        }

        if (quest.Objectives.Count == 0) failures.Add($"quest '{quest.Id}' has no objectives");

        for (var i = 0; i < quest.Objectives.Count; i++)
        {
            var obj = quest.Objectives[i];
            if (!allowedTypes.Contains(obj.Type)) failures.Add($"quest '{quest.Id}' objective[{i}] unsupported type '{obj.Type}'");
            if (obj.Count <= 0) failures.Add($"quest '{quest.Id}' objective[{i}] has invalid count '{obj.Count}'");
            if (string.IsNullOrWhiteSpace(obj.TargetId)) failures.Add($"quest '{quest.Id}' objective[{i}] missing targetId");

            if (obj.Type.Equals("CompleteEncounter", StringComparison.OrdinalIgnoreCase) && !encounters.ContainsKey(obj.TargetId))
            {
                failures.Add($"quest '{quest.Id}' objective[{i}] references missing encounter '{obj.TargetId}'");
            }

            if (obj.Type.Equals("TalkToNpc", StringComparison.OrdinalIgnoreCase) && !npcs.ContainsKey(obj.TargetId))
            {
                failures.Add($"quest '{quest.Id}' objective[{i}] references missing npc '{obj.TargetId}'");
            }

            if (obj.Type.Equals("DestroyObjectType", StringComparison.OrdinalIgnoreCase) && !objects.ContainsKey(obj.TargetId))
            {
                failures.Add($"quest '{quest.Id}' objective[{i}] references missing object '{obj.TargetId}'");
            }
        }
    }
}

static void ValidateNpcs(
    IReadOnlyDictionary<string, NpcContent> npcs,
    IReadOnlyDictionary<string, ZoneContent> zones,
    List<string> failures)
{
    if (npcs.Count == 0)
    {
        failures.Add("No npc entries found under content/world/npcs.");
        return;
    }

    foreach (var npc in npcs.Values)
    {
        if (string.IsNullOrWhiteSpace(npc.Name)) failures.Add($"npc '{npc.Id}' missing name");
        if (string.IsNullOrWhiteSpace(npc.ZoneId) || !zones.ContainsKey(npc.ZoneId))
        {
            failures.Add($"npc '{npc.Id}' has unknown zoneId '{npc.ZoneId}'");
        }

        if (npc.InteractRadiusMilli <= 0)
        {
            failures.Add($"npc '{npc.Id}' has invalid interactRadiusMilli '{npc.InteractRadiusMilli}'");
        }
    }
}

static void ValidateBestiary(IReadOnlyDictionary<string, BestiaryContent> bestiary, List<string> failures)
{
    if (bestiary.Count == 0)
    {
        failures.Add("No bestiary entries found under content/world/bestiary.");
        return;
    }

    var allowedBrains = new HashSet<string>(StringComparer.Ordinal)
    {
        "MeleeChaseAndHit",
        "RangedKiteAndShoot",
        "ControllerPullOrRoot",
        "SupportHealOrRepair",
        "AssassinDashInOut",
        "BossScriptedPattern"
    };

    foreach (var enemy in bestiary.Values)
    {
        if (enemy.RoleTags.Count == 0) failures.Add($"enemy '{enemy.Id}' has no roleTags");
        if (enemy.Stats is null) failures.Add($"enemy '{enemy.Id}' missing stats");
        else
        {
            if (enemy.Stats.Health <= 0) failures.Add($"enemy '{enemy.Id}' has invalid health '{enemy.Stats.Health}'");
            if (enemy.Stats.Speed <= 0) failures.Add($"enemy '{enemy.Id}' has invalid speed '{enemy.Stats.Speed}'");
            if (enemy.Stats.BaseDamage < 0) failures.Add($"enemy '{enemy.Id}' has invalid baseDamage '{enemy.Stats.BaseDamage}'");
        }

        if (string.IsNullOrWhiteSpace(enemy.AiBrainId) || !allowedBrains.Contains(enemy.AiBrainId))
        {
            failures.Add($"enemy '{enemy.Id}' has unsupported aiBrainId '{enemy.AiBrainId}'");
        }
    }
}

static void ValidateObjects(IReadOnlyDictionary<string, ObjectContent> objects, IReadOnlyDictionary<string, HazardContent> hazards, List<string> failures)
{
    if (objects.Count == 0)
    {
        failures.Add("No object entries found under content/world/objects.");
        return;
    }

    var allowedModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "attack_to_destroy",
        "interact_to_disable"
    };
    var allowedFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "destructible",
        "interactable",
        "spawner",
        "objective"
    };

    foreach (var obj in objects.Values)
    {
        if (obj.MaxHealth <= 0) failures.Add($"object '{obj.Id}' has invalid maxHealth '{obj.MaxHealth}'");
        if (!allowedModes.Contains(obj.InteractMode)) failures.Add($"object '{obj.Id}' has unsupported interactMode '{obj.InteractMode}'");
        if (obj.Flags.Count == 0) failures.Add($"object '{obj.Id}' should define at least one flag");
        for (var i = 0; i < obj.Flags.Count; i++)
        {
            if (!allowedFlags.Contains(obj.Flags[i]))
            {
                failures.Add($"object '{obj.Id}' has unsupported flag '{obj.Flags[i]}'");
            }
        }

        if (!string.IsNullOrWhiteSpace(obj.LinkedHazardId) && !hazards.ContainsKey(obj.LinkedHazardId))
        {
            failures.Add($"object '{obj.Id}' linkedHazardId '{obj.LinkedHazardId}' not found");
        }
    }
}

static void ValidateHazards(IReadOnlyDictionary<string, HazardContent> hazards, List<string> failures)
{
    if (hazards.Count == 0)
    {
        failures.Add("No hazard entries found under content/world/hazards.");
        return;
    }

    var allowedEffectTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "damage",
        "enemy_buff",
        "slow",
        "disrupt",
        "threat_amp"
    };

    foreach (var hz in hazards.Values)
    {
        if (string.IsNullOrWhiteSpace(hz.ZoneDefId)) failures.Add($"hazard '{hz.Id}' missing zoneDefId");
        if (hz.TickIntervalMs <= 0) failures.Add($"hazard '{hz.Id}' has invalid tickIntervalMs '{hz.TickIntervalMs}'");
        if (hz.DurationMs <= 0) failures.Add($"hazard '{hz.Id}' has invalid durationMs '{hz.DurationMs}'");
        if (hz.EffectTags.Count == 0) failures.Add($"hazard '{hz.Id}' has no effectTags");
        for (var i = 0; i < hz.EffectTags.Count; i++)
        {
            if (!allowedEffectTags.Contains(hz.EffectTags[i]))
            {
                failures.Add($"hazard '{hz.Id}' has unsupported effectTag '{hz.EffectTags[i]}'");
            }
        }
    }
}

public sealed class ActContent
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StartZoneId { get; set; } = string.Empty;
    public List<string> ZoneIds { get; set; } = new();
    public List<string> MainQuestIds { get; set; } = new();
}

public sealed class ZoneContent
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> EncounterIds { get; set; } = new();
    public string? MapFile { get; set; }
}

public sealed class QuestContent
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> PrerequisiteQuestIds { get; set; } = new();
    public List<QuestObjectiveContent> Objectives { get; set; } = new();
}

public sealed class QuestObjectiveContent
{
    public string Type { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class EncounterContent
{
    public string Id { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public List<string> EnemyIds { get; set; } = new();
    public List<string> ObjectiveObjectIds { get; set; } = new();
    public List<string> HazardIds { get; set; } = new();
    public string? CompletionEventKind { get; set; }
    public int CompletionCount { get; set; }
    public string? RewardItemCode { get; set; }
    public int RewardItemQuantity { get; set; }
}

public sealed class BestiaryContent
{
    public string Id { get; set; } = string.Empty;
    public List<string> RoleTags { get; set; } = new();
    public EnemyStatsContent? Stats { get; set; }
    public string AiBrainId { get; set; } = string.Empty;
    public string? AbilityProfileId { get; set; }
}

public sealed class EnemyStatsContent
{
    public int Health { get; set; }
    public int Speed { get; set; }
    public int BaseDamage { get; set; }
}

public sealed class ObjectContent
{
    public string Id { get; set; } = string.Empty;
    public int MaxHealth { get; set; }
    public string InteractMode { get; set; } = string.Empty;
    public List<string> Flags { get; set; } = new();
    public string? LinkedHazardId { get; set; }
}

public sealed class NpcContent
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public int InteractRadiusMilli { get; set; } = 2000;
}

public sealed class HazardContent
{
    public string Id { get; set; } = string.Empty;
    public string ZoneDefId { get; set; } = string.Empty;
    public int TickIntervalMs { get; set; }
    public int DurationMs { get; set; }
    public List<string> EffectTags { get; set; } = new();
}

public sealed class ZoneLayoutContent
{
    public string ZoneId { get; set; } = string.Empty;
    public Dictionary<string, EncounterLayoutContent> Encounters { get; set; } = new(StringComparer.Ordinal);
    public List<string> NpcIds { get; set; } = new();
}

public sealed class EncounterLayoutContent
{
    public string EncounterId { get; set; } = string.Empty;
    public List<string> ObjectDefIds { get; set; } = new();
    public List<string> HazardIds { get; set; } = new();
}
