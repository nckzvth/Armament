using System.Text.Json;

namespace Armament.ServerHost;

public sealed class LoadedWorldContent
{
    public LoadedWorldContent(
        WorldActContent act,
        IReadOnlyDictionary<string, WorldZoneContent> zones,
        IReadOnlyDictionary<string, WorldQuestContent> quests,
        IReadOnlyDictionary<string, WorldEncounterContent> encounters,
        IReadOnlyDictionary<string, WorldBestiaryContent> bestiary,
        IReadOnlyDictionary<string, WorldNpcContent> npcs,
        IReadOnlyDictionary<string, WorldObjectContent> objects,
        IReadOnlyDictionary<string, WorldZoneLayoutContent> zoneLayouts,
        IReadOnlyDictionary<string, WorldHazardContent> hazards,
        string message)
    {
        Act = act;
        Zones = zones;
        Quests = quests;
        Encounters = encounters;
        Bestiary = bestiary;
        Npcs = npcs;
        Objects = objects;
        ZoneLayouts = zoneLayouts;
        Hazards = hazards;
        Message = message;
    }

    public WorldActContent Act { get; }
    public IReadOnlyDictionary<string, WorldZoneContent> Zones { get; }
    public IReadOnlyDictionary<string, WorldQuestContent> Quests { get; }
    public IReadOnlyDictionary<string, WorldEncounterContent> Encounters { get; }
    public IReadOnlyDictionary<string, WorldBestiaryContent> Bestiary { get; }
    public IReadOnlyDictionary<string, WorldNpcContent> Npcs { get; }
    public IReadOnlyDictionary<string, WorldObjectContent> Objects { get; }
    public IReadOnlyDictionary<string, WorldZoneLayoutContent> ZoneLayouts { get; }
    public IReadOnlyDictionary<string, WorldHazardContent> Hazards { get; }
    public string Message { get; }
}

public static class WorldContentLoader
{
    public static LoadedWorldContent LoadOrFail(string repoRoot)
    {
        var contentRoot = ResolveWorldContentRoot(repoRoot)
            ?? throw new InvalidOperationException("world content root not found (content/world)");

        var act1Root = Path.Combine(contentRoot, "acts", "act1");
        var actPath = Path.Combine(act1Root, "act.json");

        var options = CreateJsonOptions();
        var act = JsonSerializer.Deserialize<WorldActContent>(File.ReadAllText(actPath), options)
            ?? throw new InvalidOperationException($"failed to parse world act payload: {actPath}");

        var zones = LoadById<WorldZoneContent>(Path.Combine(act1Root, "zones"), x => x.Id, options);
        var quests = LoadById<WorldQuestContent>(Path.Combine(act1Root, "quests"), x => x.Id, options);
        var encounters = LoadById<WorldEncounterContent>(Path.Combine(act1Root, "encounters"), x => x.Id, options);
        var bestiary = LoadByIdFromArrays<WorldBestiaryContent>(Path.Combine(contentRoot, "bestiary"), x => x.Id, options);
        var npcs = LoadByIdFromArrays<WorldNpcContent>(Path.Combine(contentRoot, "npcs"), x => x.Id, options);
        var objects = LoadByIdFromArrays<WorldObjectContent>(Path.Combine(contentRoot, "objects"), x => x.Id, options);
        var hazards = LoadByIdFromArrays<WorldHazardContent>(Path.Combine(contentRoot, "hazards"), x => x.Id, options);
        var zoneLayouts = LoadZoneLayouts(act1Root, zones);

        var failures = new List<string>();
        Validate(act, zones, quests, encounters, bestiary, npcs, objects, zoneLayouts, hazards, failures);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"World content validation failed ({failures.Count}): {string.Join(" | ", failures)}");
        }

        var message =
            $"act={act.Id}; zones={zones.Count}; quests={quests.Count}; encounters={encounters.Count}; bestiary={bestiary.Count}; npcs={npcs.Count}; objects={objects.Count}; layouts={zoneLayouts.Count}; hazards={hazards.Count}";
        return new LoadedWorldContent(act, zones, quests, encounters, bestiary, npcs, objects, zoneLayouts, hazards, message);
    }

    private static Dictionary<string, WorldZoneLayoutContent> LoadZoneLayouts(
        string actRoot,
        IReadOnlyDictionary<string, WorldZoneContent> zones)
    {
        var layouts = new Dictionary<string, WorldZoneLayoutContent>(StringComparer.Ordinal);
        foreach (var zone in zones.Values)
        {
            if (string.IsNullOrWhiteSpace(zone.MapFile))
            {
                continue;
            }

            var mapPath = Path.Combine(actRoot, zone.MapFile);
            if (!File.Exists(mapPath))
            {
                continue;
            }

            layouts[zone.Id] = TiledCampaignMapLoader.Load(zone.Id, mapPath);
        }

        return layouts;
    }

    private static string? ResolveWorldContentRoot(string repoRoot)
    {
        var direct = Path.Combine(repoRoot, "content", "world");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 10 && dir is not null; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "content", "world");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Dictionary<string, T> LoadById<T>(string dir, Func<T, string> idSelector, JsonSerializerOptions options)
    {
        if (!Directory.Exists(dir))
        {
            throw new InvalidOperationException($"missing world content directory: {dir}");
        }

        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            var payload = JsonSerializer.Deserialize<T>(File.ReadAllText(file), options)
                ?? throw new InvalidOperationException($"failed to parse JSON payload: {file}");
            var id = idSelector(payload);
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException($"missing id in world content file: {file}");
            }

            if (!map.TryAdd(id, payload))
            {
                throw new InvalidOperationException($"duplicate world content id '{id}' in {file}");
            }
        }

        return map;
    }

    private static Dictionary<string, T> LoadByIdFromArrays<T>(string dir, Func<T, string> idSelector, JsonSerializerOptions options)
    {
        if (!Directory.Exists(dir))
        {
            throw new InvalidOperationException($"missing world content directory: {dir}");
        }

        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            var payload = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(file), options)
                ?? throw new InvalidOperationException($"failed to parse array JSON payload: {file}");
            for (var i = 0; i < payload.Count; i++)
            {
                var id = idSelector(payload[i]);
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new InvalidOperationException($"missing id in world content array entry: {file} (index {i})");
                }

                if (!map.TryAdd(id, payload[i]))
                {
                    throw new InvalidOperationException($"duplicate world content id '{id}' in {file} (index {i})");
                }
            }
        }

        return map;
    }

    private static void Validate(
        WorldActContent act,
        IReadOnlyDictionary<string, WorldZoneContent> zones,
        IReadOnlyDictionary<string, WorldQuestContent> quests,
        IReadOnlyDictionary<string, WorldEncounterContent> encounters,
        IReadOnlyDictionary<string, WorldBestiaryContent> bestiary,
        IReadOnlyDictionary<string, WorldNpcContent> npcs,
        IReadOnlyDictionary<string, WorldObjectContent> objects,
        IReadOnlyDictionary<string, WorldZoneLayoutContent> zoneLayouts,
        IReadOnlyDictionary<string, WorldHazardContent> hazards,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(act.Id)) failures.Add("act.id missing");
        if (string.IsNullOrWhiteSpace(act.StartZoneId) || !zones.ContainsKey(act.StartZoneId)) failures.Add($"act.startZoneId '{act.StartZoneId}' missing zone");

        for (var i = 0; i < act.ZoneIds.Count; i++)
        {
            if (!zones.ContainsKey(act.ZoneIds[i])) failures.Add($"act references missing zone '{act.ZoneIds[i]}'");
        }

        for (var i = 0; i < act.MainQuestIds.Count; i++)
        {
            if (!quests.ContainsKey(act.MainQuestIds[i])) failures.Add($"act references missing quest '{act.MainQuestIds[i]}'");
        }

        foreach (var z in zones.Values)
        {
            if (z.EncounterIds.Count == 0) failures.Add($"zone '{z.Id}' has no encounters");
            for (var i = 0; i < z.EncounterIds.Count; i++)
            {
                if (!encounters.ContainsKey(z.EncounterIds[i])) failures.Add($"zone '{z.Id}' references missing encounter '{z.EncounterIds[i]}'");
            }

            if (!string.IsNullOrWhiteSpace(z.MapFile) && !zoneLayouts.ContainsKey(z.Id))
            {
                failures.Add($"zone '{z.Id}' mapFile '{z.MapFile}' could not be loaded");
            }
        }

        foreach (var q in quests.Values)
        {
            if (q.Objectives.Count == 0) failures.Add($"quest '{q.Id}' has no objectives");
            for (var i = 0; i < q.PrerequisiteQuestIds.Count; i++)
            {
                if (!quests.ContainsKey(q.PrerequisiteQuestIds[i])) failures.Add($"quest '{q.Id}' prerequisite missing '{q.PrerequisiteQuestIds[i]}'");
            }

            for (var i = 0; i < q.Objectives.Count; i++)
            {
                var o = q.Objectives[i];
                if (o.Count <= 0) failures.Add($"quest '{q.Id}' objective[{i}] invalid count '{o.Count}'");
                if (string.IsNullOrWhiteSpace(o.TargetId)) failures.Add($"quest '{q.Id}' objective[{i}] missing targetId");
                if (o.Type.Equals("CompleteEncounter", StringComparison.OrdinalIgnoreCase) && !encounters.ContainsKey(o.TargetId))
                {
                    failures.Add($"quest '{q.Id}' objective[{i}] missing encounter '{o.TargetId}'");
                }

                if (o.Type.Equals("DestroyObjectType", StringComparison.OrdinalIgnoreCase) && !objects.ContainsKey(o.TargetId))
                {
                    failures.Add($"quest '{q.Id}' objective[{i}] missing object '{o.TargetId}'");
                }

                if (o.Type.Equals("TalkToNpc", StringComparison.OrdinalIgnoreCase) && !npcs.ContainsKey(o.TargetId))
                {
                    failures.Add($"quest '{q.Id}' objective[{i}] missing npc '{o.TargetId}'");
                }
            }
        }

        foreach (var e in encounters.Values)
        {
            if (!zones.ContainsKey(e.ZoneId)) failures.Add($"encounter '{e.Id}' missing zone '{e.ZoneId}'");
            if (e.EnemyIds.Count == 0) failures.Add($"encounter '{e.Id}' has no enemies");

            for (var i = 0; i < e.EnemyIds.Count; i++)
            {
                if (!bestiary.ContainsKey(e.EnemyIds[i])) failures.Add($"encounter '{e.Id}' missing enemy '{e.EnemyIds[i]}'");
            }

            for (var i = 0; i < e.ObjectiveObjectIds.Count; i++)
            {
                if (!objects.ContainsKey(e.ObjectiveObjectIds[i])) failures.Add($"encounter '{e.Id}' missing object '{e.ObjectiveObjectIds[i]}'");
            }

            for (var i = 0; i < e.HazardIds.Count; i++)
            {
                if (!hazards.ContainsKey(e.HazardIds[i])) failures.Add($"encounter '{e.Id}' missing hazard '{e.HazardIds[i]}'");
            }

            if (!string.IsNullOrWhiteSpace(e.CompletionEventKind))
            {
                if (!e.CompletionEventKind.Equals("EnemyKilled", StringComparison.OrdinalIgnoreCase) &&
                    !e.CompletionEventKind.Equals("ObjectDestroyed", StringComparison.OrdinalIgnoreCase) &&
                    !e.CompletionEventKind.Equals("ObjectiveCompleted", StringComparison.OrdinalIgnoreCase) &&
                    !e.CompletionEventKind.Equals("TokenCollected", StringComparison.OrdinalIgnoreCase) &&
                    !e.CompletionEventKind.Equals("PlayerEnteredRegion", StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"encounter '{e.Id}' has unsupported completionEventKind '{e.CompletionEventKind}'");
                }

                if (e.CompletionCount <= 0)
                {
                    failures.Add($"encounter '{e.Id}' has completionEventKind but invalid completionCount '{e.CompletionCount}'");
                }
            }

            if (!string.IsNullOrWhiteSpace(e.RewardItemCode) && e.RewardItemQuantity <= 0)
            {
                failures.Add($"encounter '{e.Id}' has rewardItemCode but invalid rewardItemQuantity '{e.RewardItemQuantity}'");
            }

            if (e.CompletionCount < 0)
            {
                failures.Add($"encounter '{e.Id}' has invalid completionCount '{e.CompletionCount}'");
            }
        }

        foreach (var b in bestiary.Values)
        {
            if (string.IsNullOrWhiteSpace(b.AiBrainId)) failures.Add($"bestiary '{b.Id}' missing aiBrainId");
            if (b.Stats is null || b.Stats.Health <= 0 || b.Stats.Speed <= 0) failures.Add($"bestiary '{b.Id}' has invalid stats");
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

        foreach (var o in objects.Values)
        {
            if (o.MaxHealth <= 0) failures.Add($"object '{o.Id}' invalid maxHealth");
            if (o.Flags.Count == 0) failures.Add($"object '{o.Id}' has no flags");
            for (var i = 0; i < o.Flags.Count; i++)
            {
                if (!o.Flags[i].Equals("destructible", StringComparison.OrdinalIgnoreCase) &&
                    !o.Flags[i].Equals("interactable", StringComparison.OrdinalIgnoreCase) &&
                    !o.Flags[i].Equals("spawner", StringComparison.OrdinalIgnoreCase) &&
                    !o.Flags[i].Equals("objective", StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"object '{o.Id}' has unsupported flag '{o.Flags[i]}'");
                }
            }
            if (!string.IsNullOrWhiteSpace(o.LinkedHazardId) && !hazards.ContainsKey(o.LinkedHazardId))
            {
                failures.Add($"object '{o.Id}' linkedHazardId '{o.LinkedHazardId}' missing");
            }
        }

        foreach (var h in hazards.Values)
        {
            if (string.IsNullOrWhiteSpace(h.ZoneDefId)) failures.Add($"hazard '{h.Id}' missing zoneDefId");
            if (h.TickIntervalMs <= 0 || h.DurationMs <= 0) failures.Add($"hazard '{h.Id}' has invalid timing");
            if (h.EffectTags.Count == 0) failures.Add($"hazard '{h.Id}' has no effectTags");
            for (var i = 0; i < h.EffectTags.Count; i++)
            {
                if (!h.EffectTags[i].Equals("damage", StringComparison.OrdinalIgnoreCase) &&
                    !h.EffectTags[i].Equals("enemy_buff", StringComparison.OrdinalIgnoreCase) &&
                    !h.EffectTags[i].Equals("slow", StringComparison.OrdinalIgnoreCase) &&
                    !h.EffectTags[i].Equals("disrupt", StringComparison.OrdinalIgnoreCase) &&
                    !h.EffectTags[i].Equals("threat_amp", StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"hazard '{h.Id}' has unsupported effectTag '{h.EffectTags[i]}'");
                }
            }
        }

        foreach (var zoneLayout in zoneLayouts.Values)
        {
            if (!zones.TryGetValue(zoneLayout.ZoneId, out var zone))
            {
                failures.Add($"layout references unknown zone '{zoneLayout.ZoneId}'");
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

                if (!zoneLayout.Encounters.ContainsKey(encounterId))
                {
                    failures.Add($"layout zone '{zoneLayout.ZoneId}' missing encounter entry '{encounterId}'");
                }
            }

            foreach (var encounterLayout in zoneLayout.Encounters.Values)
            {
                if (!encounters.TryGetValue(encounterLayout.EncounterId, out var encounter))
                {
                    failures.Add($"layout zone '{zoneLayout.ZoneId}' references unknown encounter '{encounterLayout.EncounterId}'");
                    continue;
                }

                if (!zoneEncounterIds.Contains(encounterLayout.EncounterId))
                {
                    failures.Add($"layout zone '{zoneLayout.ZoneId}' encounter '{encounterLayout.EncounterId}' not in zone encounter list");
                }

                var encounterObjects = new HashSet<string>(encounter.ObjectiveObjectIds, StringComparer.Ordinal);
                var placedObjects = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < encounterLayout.ObjectPlacements.Count; i++)
                {
                    var placement = encounterLayout.ObjectPlacements[i];
                    if (!objects.ContainsKey(placement.ObjectDefId))
                    {
                        failures.Add($"layout encounter '{encounterLayout.EncounterId}' unknown objectDefId '{placement.ObjectDefId}'");
                    }
                    else if (!encounterObjects.Contains(placement.ObjectDefId))
                    {
                        failures.Add($"layout encounter '{encounterLayout.EncounterId}' object '{placement.ObjectDefId}' not listed in objectiveObjectIds");
                    }
                    else
                    {
                        placedObjects.Add(placement.ObjectDefId);
                    }
                }

                var encounterHazards = new HashSet<string>(encounter.HazardIds, StringComparer.Ordinal);
                var placedHazards = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < encounterLayout.HazardPlacements.Count; i++)
                {
                    var placement = encounterLayout.HazardPlacements[i];
                    if (!hazards.ContainsKey(placement.HazardId))
                    {
                        failures.Add($"layout encounter '{encounterLayout.EncounterId}' unknown hazardId '{placement.HazardId}'");
                    }
                    else if (!encounterHazards.Contains(placement.HazardId))
                    {
                        failures.Add($"layout encounter '{encounterLayout.EncounterId}' hazard '{placement.HazardId}' not listed in hazardIds");
                    }
                    else
                    {
                        placedHazards.Add(placement.HazardId);
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
            for (var i = 0; i < zoneLayout.NpcPlacements.Count; i++)
            {
                var placement = zoneLayout.NpcPlacements[i];
                if (!npcs.TryGetValue(placement.NpcId, out var npc))
                {
                    failures.Add($"layout zone '{zoneLayout.ZoneId}' unknown npcId '{placement.NpcId}'");
                    continue;
                }

                if (!string.Equals(npc.ZoneId, zoneLayout.ZoneId, StringComparison.Ordinal))
                {
                    failures.Add($"layout zone '{zoneLayout.ZoneId}' npc '{placement.NpcId}' belongs to zone '{npc.ZoneId}'");
                }

                if (!placedNpcIds.Add(placement.NpcId))
                {
                    failures.Add($"layout zone '{zoneLayout.ZoneId}' duplicate npc placement '{placement.NpcId}'");
                }
            }

            foreach (var npc in npcs.Values)
            {
                if (!string.Equals(npc.ZoneId, zoneLayout.ZoneId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!placedNpcIds.Contains(npc.Id))
                {
                    failures.Add($"layout zone '{zoneLayout.ZoneId}' missing placement for npc '{npc.Id}'");
                }
            }
        }
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

public sealed class WorldActContent
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StartZoneId { get; set; } = string.Empty;
    public List<string> ZoneIds { get; set; } = new();
    public List<string> MainQuestIds { get; set; } = new();
}

public sealed class WorldZoneContent
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> EncounterIds { get; set; } = new();
    public string? MapFile { get; set; }
}

public sealed class WorldQuestContent
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> PrerequisiteQuestIds { get; set; } = new();
    public List<WorldQuestObjectiveContent> Objectives { get; set; } = new();
}

public sealed class WorldQuestObjectiveContent
{
    public string Type { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class WorldEncounterContent
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

public sealed class WorldBestiaryContent
{
    public string Id { get; set; } = string.Empty;
    public List<string> RoleTags { get; set; } = new();
    public WorldEnemyStatsContent? Stats { get; set; }
    public string AiBrainId { get; set; } = string.Empty;
    public string? AbilityProfileId { get; set; }
}

public sealed class WorldEnemyStatsContent
{
    public int Health { get; set; }
    public int Speed { get; set; }
    public int BaseDamage { get; set; }
}

public sealed class WorldObjectContent
{
    public string Id { get; set; } = string.Empty;
    public int MaxHealth { get; set; }
    public string InteractMode { get; set; } = string.Empty;
    public List<string> Flags { get; set; } = new();
    public string? LinkedHazardId { get; set; }
}

public sealed class WorldNpcContent
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public int InteractRadiusMilli { get; set; } = 2000;
}

public sealed class WorldHazardContent
{
    public string Id { get; set; } = string.Empty;
    public string ZoneDefId { get; set; } = string.Empty;
    public int TickIntervalMs { get; set; }
    public int DurationMs { get; set; }
    public List<string> EffectTags { get; set; } = new();
}
