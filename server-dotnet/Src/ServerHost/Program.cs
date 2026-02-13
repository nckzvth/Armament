using Armament.GameServer;
using Armament.GameServer.Campaign;
using Armament.ServerHost;

var port = 9000;
var simulationHz = 60;
var snapshotHz = 20;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port":
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedPort))
            {
                port = parsedPort;
                i++;
            }
            break;
        case "--simulation-hz":
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedSimHz))
            {
                simulationHz = parsedSimHz;
                i++;
            }
            break;
        case "--snapshot-hz":
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedSnapshotHz))
            {
                snapshotHz = parsedSnapshotHz;
                i++;
            }
            break;
    }
}

var dbConnection = Environment.GetEnvironmentVariable("ARMAMENT_DB_CONNECTION");
var repoRoot = Directory.GetCurrentDirectory();
var abilityProfiles = ContentAbilityProfileLoader.LoadOrFallback(repoRoot, simulationHz);
var worldContent = WorldContentLoader.LoadOrFail(repoRoot);
var campPerimeterDefs = BuildCampaignEncounterDefinitions(worldContent);
var campaignWorldDefinition = BuildCampaignWorldDefinition(worldContent, simulationHz);
PersistenceBackedLootSink? persistenceBackedLootSink = null;
PersistenceBackedCharacterProfileService? characterProfileService = null;

if (!string.IsNullOrWhiteSpace(dbConnection))
{
    persistenceBackedLootSink = new PersistenceBackedLootSink(dbConnection);
    await persistenceBackedLootSink.InitializeAsync(CancellationToken.None);
    characterProfileService = new PersistenceBackedCharacterProfileService(dbConnection);
    Console.WriteLine("[Server] Persistence queue enabled.");
}
else
{
    Console.WriteLine("[Server] Persistence queue disabled (set ARMAMENT_DB_CONNECTION to enable).");
}

await using var server = new AuthoritativeServer(
    port,
    simulationHz,
    snapshotHz,
    persistenceBackedLootSink?.Sink,
    characterProfileService,
    abilityProfiles,
    campPerimeterDefs,
    campaignWorldDefinition);
server.Start();
Console.WriteLine($"[Server] Ability profiles: {abilityProfiles.Message}");
Console.WriteLine($"[Server] World content: {worldContent.Message}");

Console.WriteLine("[Server] Press Ctrl+C to stop.");

var stop = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stop.Set();
};

stop.Wait();

if (persistenceBackedLootSink is not null)
{
    Console.WriteLine($"[Server] Persistence queue stats: processed={persistenceBackedLootSink.ProcessedCount}, dropped={persistenceBackedLootSink.DroppedCount}, failed={persistenceBackedLootSink.FailedCount}");
    await persistenceBackedLootSink.DisposeAsync();
}

if (characterProfileService is not null)
{
    await characterProfileService.DisposeAsync();
}

static IReadOnlyDictionary<string, CampaignEncounterDefinition> BuildCampaignEncounterDefinitions(LoadedWorldContent content)
{
    var defs = new Dictionary<string, CampaignEncounterDefinition>(StringComparer.Ordinal);
    foreach (var encounter in content.Encounters.Values)
    {
        var kind = encounter.CompletionEventKind?.ToLowerInvariant() switch
        {
            "tokencollected" => CampaignCompletionEventKind.TokenCollected,
            "objectdestroyed" => CampaignCompletionEventKind.ObjectDestroyed,
            "objectivecompleted" => CampaignCompletionEventKind.ObjectiveCompleted,
            "playerenteredregion" => CampaignCompletionEventKind.PlayerEnteredRegion,
            _ => CampaignCompletionEventKind.EnemyKilled
        };

        defs[encounter.Id] = new CampaignEncounterDefinition
        {
            Id = encounter.Id,
            ZoneId = encounter.ZoneId,
            CompletionEventKind = kind,
            CompletionCount = Math.Max(1, encounter.CompletionCount),
            RewardItemCode = encounter.RewardItemCode,
            RewardItemQuantity = Math.Max(1, encounter.RewardItemQuantity <= 0 ? 1 : encounter.RewardItemQuantity),
            EnemyIds = new List<string>(encounter.EnemyIds),
            ObjectiveObjectIds = new List<string>(encounter.ObjectiveObjectIds),
            HazardIds = new List<string>(encounter.HazardIds)
        };
    }

    return defs;
}

static CampaignWorldDefinition BuildCampaignWorldDefinition(LoadedWorldContent content, int simulationHz)
{
    var encounters = new Dictionary<string, CampaignEncounterDefinition>(StringComparer.Ordinal);
    foreach (var encounter in content.Encounters.Values)
    {
        var eventKind = encounter.CompletionEventKind?.ToLowerInvariant() switch
        {
            "tokencollected" => CampaignCompletionEventKind.TokenCollected,
            "objectdestroyed" => CampaignCompletionEventKind.ObjectDestroyed,
            "objectivecompleted" => CampaignCompletionEventKind.ObjectiveCompleted,
            "playerenteredregion" => CampaignCompletionEventKind.PlayerEnteredRegion,
            _ => CampaignCompletionEventKind.EnemyKilled
        };

        encounters[encounter.Id] = new CampaignEncounterDefinition
        {
            Id = encounter.Id,
            ZoneId = encounter.ZoneId,
            CompletionEventKind = eventKind,
            CompletionCount = Math.Max(1, encounter.CompletionCount <= 0 ? 1 : encounter.CompletionCount),
            RewardItemCode = encounter.RewardItemCode,
            RewardItemQuantity = Math.Max(1, encounter.RewardItemQuantity <= 0 ? 1 : encounter.RewardItemQuantity),
            EnemyIds = new List<string>(encounter.EnemyIds),
            ObjectiveObjectIds = new List<string>(encounter.ObjectiveObjectIds),
            HazardIds = new List<string>(encounter.HazardIds)
        };
    }

    var quests = new Dictionary<string, CampaignQuestDefinition>(StringComparer.Ordinal);
    foreach (var quest in content.Quests.Values)
    {
        var definition = new CampaignQuestDefinition
        {
            Id = quest.Id,
            Title = quest.Title,
            PrerequisiteQuestIds = new List<string>(quest.PrerequisiteQuestIds)
        };

        for (var i = 0; i < quest.Objectives.Count; i++)
        {
            var objective = quest.Objectives[i];
            definition.Objectives.Add(new CampaignQuestObjectiveDefinition
            {
                Type = objective.Type,
                TargetId = objective.TargetId,
                Count = Math.Max(1, objective.Count)
            });
        }

        quests[definition.Id] = definition;
    }

    var zones = new Dictionary<string, CampaignZoneDefinition>(StringComparer.Ordinal);
    foreach (var zone in content.Zones.Values)
    {
        zones[zone.Id] = new CampaignZoneDefinition
        {
            Id = zone.Id,
            EncounterIds = new List<string>(zone.EncounterIds)
        };
    }

    var objects = new Dictionary<string, CampaignObjectDefinition>(StringComparer.Ordinal);
    foreach (var obj in content.Objects.Values)
    {
        objects[obj.Id] = new CampaignObjectDefinition
        {
            Id = obj.Id,
            MaxHealth = Math.Max(1, obj.MaxHealth),
            InteractMode = obj.InteractMode,
            Flags = new List<string>(obj.Flags),
            LinkedHazardId = obj.LinkedHazardId
        };
    }

    var npcs = new Dictionary<string, CampaignNpcDefinition>(StringComparer.Ordinal);
    foreach (var npc in content.Npcs.Values)
    {
        npcs[npc.Id] = new CampaignNpcDefinition
        {
            Id = npc.Id,
            Name = npc.Name,
            ZoneId = npc.ZoneId,
            InteractRadiusMilli = Math.Max(200, npc.InteractRadiusMilli)
        };
    }

    var hazards = new Dictionary<string, CampaignHazardDefinition>(StringComparer.Ordinal);
    foreach (var hazard in content.Hazards.Values)
    {
        hazards[hazard.Id] = new CampaignHazardDefinition
        {
            Id = hazard.Id,
            TickIntervalTicks = MillisToTicks(hazard.TickIntervalMs, simulationHz, fallback: 20),
            DurationTicks = MillisToTicks(hazard.DurationMs, simulationHz, fallback: 240),
            EffectTags = new List<string>(hazard.EffectTags)
        };
    }

    var layouts = new Dictionary<string, CampaignZoneLayoutDefinition>(StringComparer.Ordinal);
    foreach (var zoneLayout in content.ZoneLayouts.Values)
    {
        var layout = new CampaignZoneLayoutDefinition
        {
            ZoneId = zoneLayout.ZoneId
        };

        foreach (var encounterLayout in zoneLayout.Encounters.Values)
        {
            var encounter = new CampaignEncounterLayoutDefinition
            {
                EncounterId = encounterLayout.EncounterId,
                AnchorXMilli = encounterLayout.AnchorXMilli,
                AnchorYMilli = encounterLayout.AnchorYMilli
            };

            for (var i = 0; i < encounterLayout.ObjectPlacements.Count; i++)
            {
                encounter.ObjectPlacements.Add(new CampaignObjectPlacementDefinition
                {
                    ObjectDefId = encounterLayout.ObjectPlacements[i].ObjectDefId,
                    XMilli = encounterLayout.ObjectPlacements[i].XMilli,
                    YMilli = encounterLayout.ObjectPlacements[i].YMilli
                });
            }

            for (var i = 0; i < encounterLayout.HazardPlacements.Count; i++)
            {
                encounter.HazardPlacements.Add(new CampaignHazardPlacementDefinition
                {
                    HazardId = encounterLayout.HazardPlacements[i].HazardId,
                    XMilli = encounterLayout.HazardPlacements[i].XMilli,
                    YMilli = encounterLayout.HazardPlacements[i].YMilli
                });
            }

            layout.Encounters[encounter.EncounterId] = encounter;
        }

        for (var i = 0; i < zoneLayout.NpcPlacements.Count; i++)
        {
            layout.NpcPlacements.Add(new CampaignNpcPlacementDefinition
            {
                NpcId = zoneLayout.NpcPlacements[i].NpcId,
                XMilli = zoneLayout.NpcPlacements[i].XMilli,
                YMilli = zoneLayout.NpcPlacements[i].YMilli
            });
        }

        layouts[layout.ZoneId] = layout;
    }

    return new CampaignWorldDefinition
    {
        StartZoneId = content.Act.StartZoneId,
        Zones = zones,
        Encounters = encounters,
        Quests = quests,
        Npcs = npcs,
        Objects = objects,
        Hazards = hazards,
        ZoneLayouts = layouts
    };
}

static int MillisToTicks(int millis, int simulationHz, int fallback)
{
    if (millis <= 0 || simulationHz <= 0)
    {
        return fallback;
    }

    return Math.Max(1, (int)Math.Ceiling(millis / (1000m / simulationHz)));
}
