using System;
using System.Collections.Generic;
using Armament.GameServer.Campaign;
using Armament.SharedSim.Protocol;
using Armament.SharedSim.Sim;

namespace Armament.GameServer;

public sealed class OverworldZone
{
    public const uint PortalEntityId = 900_001;

    private readonly Dictionary<uint, uint> _entityByClient = new();
    private readonly OverworldSimState _simState = new();
    private readonly OverworldSimRules _simRules;
    private readonly CharacterStatTuning _statTuning;
    private readonly List<SimLootGrantEvent> _lastLootGrantEvents = new();
    private readonly List<SimEventRecord> _lastSimEvents = new();
    private readonly string _fallbackAbilityProfileId;
    private readonly CampaignWorldDefinition? _campaignWorldDefinition;

    private readonly Dictionary<uint, string> _objectDefIdByRuntimeId = new();
    private readonly Dictionary<uint, string> _objectEncounterIdByRuntimeId = new();
    private readonly Dictionary<string, List<uint>> _runtimeObjectIdsByEncounter = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, CampaignHazardRuntimeState> _activeHazards = new();
    private readonly HashSet<string> _activatedEncounterIds = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _enemyArchetypeByEntityId = new();
    private readonly Dictionary<uint, CampaignNpcRuntimeState> _activeNpcs = new();
    private readonly Dictionary<uint, string> _npcIdByRuntimeNpcId = new();
    private readonly HashSet<string> _spawnedNpcIds = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, uint> _lastNpcInteractTickByPair = new();

    private uint _nextEntityId = 1;
    private uint _nextHazardRuntimeId = 1;
    private uint _nextNpcRuntimeId = 1;
    private bool _enemySpawned;
    private bool _testLootSpawned;

    public OverworldZone(
        int simulationHz,
        IReadOnlyDictionary<string, SimAbilityProfile>? abilityProfiles = null,
        IReadOnlyDictionary<string, SimZoneDefinition>? zoneDefinitions = null,
        IReadOnlyDictionary<string, SimLinkDefinition>? linkDefinitions = null,
        string? fallbackAbilityProfileId = null,
        CampaignWorldDefinition? campaignWorldDefinition = null)
    {
        _simRules = OverworldSimRules.Default;
        _simRules.SimulationHz = simulationHz;
        _statTuning = CharacterStatTuning.Default;
        _simState.RegisterAbilityProfile(SimAbilityProfiles.BuiltinV1);
        if (abilityProfiles is not null)
        {
            foreach (var profile in abilityProfiles.Values)
            {
                _simState.RegisterAbilityProfile(profile);
            }
        }

        if (zoneDefinitions is not null)
        {
            foreach (var zone in zoneDefinitions.Values)
            {
                _simState.RegisterZoneDefinition(zone);
            }
        }

        if (linkDefinitions is not null)
        {
            foreach (var link in linkDefinitions.Values)
            {
                _simState.RegisterLinkDefinition(link);
            }
        }

        _fallbackAbilityProfileId = ResolveAbilityProfileId(fallbackAbilityProfileId);
        _simState.DefaultAbilityProfileId = _fallbackAbilityProfileId;
        _campaignWorldDefinition = campaignWorldDefinition;
        BootstrapCampaignRuntime();
    }

    public int PlayerCount => _entityByClient.Count;
    public ZoneKind ZoneKind => ZoneKind.Overworld;
    public uint InstanceId => 0;
    public IReadOnlyList<SimLootGrantEvent> LastLootGrantEvents => _lastLootGrantEvents;
    public IReadOnlyList<SimEventRecord> LastSimEvents => _lastSimEvents;

    public uint Join(uint clientId, string name, string? abilityProfileId = null)
    {
        if (_entityByClient.TryGetValue(clientId, out var existingEntityId))
        {
            return existingEntityId;
        }

        if (_campaignWorldDefinition is null && !_enemySpawned)
        {
            SpawnFallbackEnemy();
            _enemySpawned = true;
        }

        if (!_testLootSpawned)
        {
            // Non-gold click-only test drop to make click-loot verification deterministic.
            _simState.SpawnLoot(1_500, -1_500, currencyAmount: 0, autoLoot: false);
            _testLootSpawned = true;
        }

        var entityId = _nextEntityId++;
        var spawnOffsetMilli = _entityByClient.Count * 2_000;

        var entity = new SimEntityState
        {
            EntityId = entityId,
            Kind = EntityKind.Player,
            PositionXMilli = spawnOffsetMilli,
            PositionYMilli = 0,
            Health = 100,
            AbilityProfileId = ResolveAbilityProfileId(abilityProfileId)
        };
        entity.Character.CharacterId = clientId;
        entity.Character.Attributes = CharacterAttributes.Default;
        entity.Character.RecalculateDerivedStats(_statTuning);
        entity.Health = entity.Character.DerivedStats.MaxHealth;
        entity.BuilderResource = 0;
        entity.SpenderResource = entity.Character.DerivedStats.MaxSpenderResource * 1000 / 2;

        _simState.UpsertEntity(entity);
        _entityByClient[clientId] = entityId;

        return entityId;
    }

    public uint JoinTransferred(uint clientId, in PlayerTransferState transferState)
    {
        var entityId = _nextEntityId++;
        var entity = new SimEntityState
        {
            EntityId = entityId,
            Kind = EntityKind.Player,
            PositionXMilli = 0,
            PositionYMilli = 0
        };

        ApplyTransferState(entity, transferState);
        entity.AbilityProfileId = ResolveAbilityProfileId(transferState.AbilityProfileId);
        _simState.UpsertEntity(entity);
        _entityByClient[clientId] = entityId;
        return entityId;
    }

    public bool TryTransferOut(uint clientId, out PlayerTransferState transferState)
    {
        transferState = default;
        if (!_entityByClient.TryGetValue(clientId, out var entityId))
        {
            return false;
        }

        if (!_simState.TryGetEntity(entityId, out var entity))
        {
            return false;
        }

        transferState = BuildTransferState(entity);
        _simState.RemoveEntity(entityId);
        _entityByClient.Remove(clientId);
        return true;
    }

    public bool RemoveClient(uint clientId)
    {
        if (!_entityByClient.TryGetValue(clientId, out var entityId))
        {
            return false;
        }

        _enemyArchetypeByEntityId.Remove(entityId);
        _simState.RemoveEntity(entityId);
        _entityByClient.Remove(clientId);
        return true;
    }

    public bool CanUseDungeonPortal(uint clientId)
    {
        if (!_entityByClient.TryGetValue(clientId, out var entityId))
        {
            return false;
        }

        if (!_simState.TryGetEntity(entityId, out var entity))
        {
            return false;
        }

        var portalX = 6_000;
        var portalY = 0;
        var dx = entity.PositionXMilli - portalX;
        var dy = entity.PositionYMilli - portalY;
        return ((long)dx * dx + (long)dy * dy) <= (long)2_000 * 2_000;
    }

    public void ApplyInput(uint clientId, short x, short y, InputActionFlags actionFlags)
    {
        if (!_entityByClient.TryGetValue(clientId, out var entityId))
        {
            return;
        }

        if (_simState.TryGetEntity(entityId, out var entity))
        {
            entity.InputX = x;
            entity.InputY = y;
            entity.ActionFlags = actionFlags;
        }
    }

    public void Step()
    {
        OverworldSimulator.Step(_simState, _simRules);
        TickCampaignObjectInteractions();
        TickCampaignNpcInteractions();
        TickCampaignHazards();

        _lastLootGrantEvents.Clear();
        for (var i = 0; i < _simState.LootGrantEvents.Count; i++)
        {
            _lastLootGrantEvents.Add(_simState.LootGrantEvents[i]);
        }

        _lastSimEvents.Clear();
        for (var i = 0; i < _simState.SimEvents.Count; i++)
        {
            _lastSimEvents.Add(_simState.SimEvents[i]);
        }
    }

    public bool TryApplyPersistentProfile(uint clientId, in Armament.GameServer.Persistence.CharacterProfileData profile)
    {
        if (!_entityByClient.TryGetValue(clientId, out var entityId) || !_simState.TryGetEntity(entityId, out var entity))
        {
            return false;
        }

        entity.Character.Level = (ushort)Math.Clamp(profile.Level, 1, ushort.MaxValue);
        entity.Character.Experience = profile.Experience;
        entity.Character.Currency = profile.Currency;
        entity.Character.Attributes = profile.Attributes;
        entity.Character.RecalculateDerivedStats(_statTuning);
        entity.Health = entity.Character.DerivedStats.MaxHealth;
        entity.BuilderResource = 0;
        entity.SpenderResource = entity.Character.DerivedStats.MaxSpenderResource * 1000 / 2;
        entity.AbilityProfileId = ResolveAbilityProfileId(profile.SpecId);
        return true;
    }

    public bool TryResolveClientIdByEntity(uint entityId, out uint clientId)
    {
        foreach (var kvp in _entityByClient)
        {
            if (kvp.Value == entityId)
            {
                clientId = kvp.Key;
                return true;
            }
        }

        clientId = 0;
        return false;
    }

    public void SyncActivatedEncounters(IReadOnlyCollection<string> activeEncounterIds)
    {
        if (_campaignWorldDefinition is null || activeEncounterIds.Count == 0)
        {
            return;
        }

        var ordered = new List<string>(activeEncounterIds);
        ordered.Sort(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Count; i++)
        {
            var encounterId = ordered[i];
            if (_activatedEncounterIds.Contains(encounterId))
            {
                continue;
            }

            ActivateEncounter(encounterId, ResolveEncounterIndex(encounterId));
            _activatedEncounterIds.Add(encounterId);
        }
    }

    public string? TryResolveObjectDefIdByRuntimeObjectId(uint objectId)
    {
        return _objectDefIdByRuntimeId.TryGetValue(objectId, out var objectDefId)
            ? objectDefId
            : null;
    }

    public string? TryResolveNpcIdByRuntimeNpcId(uint npcRuntimeId)
    {
        return _npcIdByRuntimeNpcId.TryGetValue(npcRuntimeId, out var npcId)
            ? npcId
            : null;
    }

    public WorldSnapshot BuildSnapshot(uint serverTick)
    {
        var snapshot = new WorldSnapshot { ServerTick = serverTick, ZoneKind = ZoneKind.Overworld, InstanceId = 0 };

        AppendEntitySnapshots(snapshot);
        AppendZoneSnapshots(snapshot);
        AppendLinkSnapshots(snapshot);
        AppendWorldObjectSnapshots(snapshot);
        AppendHazardSnapshots(snapshot);
        AppendNpcSnapshots(snapshot);

        // Portal marker is represented as static loot-like marker in phase 4 debug visuals.
        snapshot.Entities.Add(new EntitySnapshot
        {
            EntityId = PortalEntityId,
            Kind = EntityKind.Loot,
            QuantizedX = Quantization.QuantizePosition(6.0f),
            QuantizedY = Quantization.QuantizePosition(0f),
            Health = 1,
            BuilderResource = 0,
            SpenderResource = 0,
            Currency = 0
        });

        return snapshot;
    }

    public uint ComputeWorldHash() => _simState.ComputeWorldHash();

    private void AppendEntitySnapshots(WorldSnapshot snapshot)
    {
        var ids = new List<uint>(_simState.Entities.Keys);
        ids.Sort();

        foreach (var entityId in ids)
        {
            var entity = _simState.Entities[entityId];
            if (!entity.IsAlive && entity.Kind != EntityKind.Player)
            {
                continue;
            }

            snapshot.Entities.Add(new EntitySnapshot
            {
                EntityId = entity.EntityId,
                Kind = entity.Kind,
                QuantizedX = Quantization.QuantizePosition(entity.PositionXMilli / 1000f),
                QuantizedY = Quantization.QuantizePosition(entity.PositionYMilli / 1000f),
                Health = (ushort)ClampUShort(entity.Health),
                BuilderResource = (ushort)ClampUShort(entity.BuilderResource / 1000),
                SpenderResource = (ushort)ClampUShort(entity.SpenderResource / 1000),
                Currency = (ushort)ClampUShort(entity.Character.Currency),
                FastCooldownTicks = (ushort)ClampUShort(entity.FastAttackCooldownTicks),
                HeavyCooldownTicks = (ushort)ClampUShort(entity.HeavyAttackCooldownTicks),
                Skill1CooldownTicks = (ushort)ClampUShort(entity.SkillCooldownTicks[0]),
                Skill2CooldownTicks = (ushort)ClampUShort(entity.SkillCooldownTicks[1]),
                Skill3CooldownTicks = (ushort)ClampUShort(entity.SkillCooldownTicks[2]),
                Skill4CooldownTicks = (ushort)ClampUShort(entity.SkillCooldownTicks[3]),
                Skill5CooldownTicks = (ushort)ClampUShort(entity.SkillCooldownTicks[4]),
                Skill6CooldownTicks = (ushort)ClampUShort(entity.SkillCooldownTicks[5]),
                Skill7CooldownTicks = (ushort)ClampUShort(entity.SkillCooldownTicks[6]),
                Skill8CooldownTicks = (ushort)ClampUShort(entity.SkillCooldownTicks[7]),
                AggroTargetEntityId = ResolveAggroTarget(entity),
                AggroThreatValue = ResolveAggroThreat(entity),
                ForcedTargetTicks = ClampByte(entity.ForcedTargetTicks),
                DebugPrimaryStatusStacks = ResolvePrimaryStatusStacks(entity),
                DebugConsumedStatusStacks = ClampByte(entity.DebugLastConsumedStatusStacks),
                DebugLastCastSlotCode = ClampByte(entity.DebugLastCastSlotCode),
                DebugLastCastResultCode = ClampByte(entity.DebugLastCastResultCode),
                DebugLastCastTargetTeamCode = ClampByte(entity.DebugLastCastTargetTeamCode),
                DebugLastCastAffectedCount = ClampByte(entity.DebugLastCastAffectedCount),
                DebugLastCastVfxCode = (ushort)ClampUShort(entity.DebugLastCastVfxCode),
                DebugLastCastFeedbackTicks = ClampByte(entity.DebugLastCastFeedbackTicks),
                ArchetypeId = ResolveEntityArchetypeId(entity)
            });
        }

        foreach (var loot in _simState.LootDrops.Values)
        {
            if (loot.Claimed)
            {
                continue;
            }

            snapshot.Entities.Add(new EntitySnapshot
            {
                EntityId = loot.LootId,
                Kind = EntityKind.Loot,
                QuantizedX = Quantization.QuantizePosition(loot.PositionXMilli / 1000f),
                QuantizedY = Quantization.QuantizePosition(loot.PositionYMilli / 1000f),
                Health = 1,
                BuilderResource = 0,
                SpenderResource = 0,
                Currency = (ushort)ClampUShort(loot.CurrencyAmount)
            });
        }
    }

    private void AppendZoneSnapshots(WorldSnapshot snapshot)
    {
        foreach (var zone in _simState.Zones.Values)
        {
            snapshot.Zones.Add(new WorldZoneSnapshot
            {
                ZoneRuntimeId = zone.ZoneId,
                ZoneDefId = zone.ZoneDefId,
                QuantizedX = Quantization.QuantizePosition(zone.PositionXMilli / 1000f),
                QuantizedY = Quantization.QuantizePosition(zone.PositionYMilli / 1000f),
                RemainingTicks = (ushort)ClampUShort(zone.RemainingTicks),
                RadiusDeciUnits = (ushort)ClampUShort(zone.RadiusMilli / 100)
            });
        }
    }

    private void AppendLinkSnapshots(WorldSnapshot snapshot)
    {
        foreach (var link in _simState.Links.Values)
        {
            if (!_simState.TryGetEntity(link.OwnerEntityId, out var owner) || !_simState.TryGetEntity(link.TargetEntityId, out var target))
            {
                continue;
            }

            var midX = (owner.PositionXMilli + target.PositionXMilli) / 2;
            var midY = (owner.PositionYMilli + target.PositionYMilli) / 2;
            snapshot.Links.Add(new WorldLinkSnapshot
            {
                LinkRuntimeId = link.LinkId,
                LinkDefId = link.LinkDefId,
                OwnerEntityId = link.OwnerEntityId,
                TargetEntityId = link.TargetEntityId,
                QuantizedX = Quantization.QuantizePosition(midX / 1000f),
                QuantizedY = Quantization.QuantizePosition(midY / 1000f),
                RemainingTicks = (ushort)ClampUShort(link.RemainingTicks)
            });
        }
    }

    private void AppendWorldObjectSnapshots(WorldSnapshot snapshot)
    {
        var objectIds = new List<uint>(_simState.WorldObjects.Keys);
        objectIds.Sort();
        for (var i = 0; i < objectIds.Count; i++)
        {
            if (!_simState.WorldObjects.TryGetValue(objectIds[i], out var obj))
            {
                continue;
            }

            _ = _objectDefIdByRuntimeId.TryGetValue(obj.ObjectId, out var objectDefId);
            _ = _objectEncounterIdByRuntimeId.TryGetValue(obj.ObjectId, out var encounterId);

            snapshot.WorldObjects.Add(new WorldObjectSnapshot
            {
                ObjectId = obj.ObjectId,
                ObjectDefId = objectDefId ?? obj.ObjectDefId,
                Archetype = ResolveObjectArchetype(objectDefId ?? obj.ObjectDefId),
                EncounterId = encounterId ?? string.Empty,
                QuantizedX = Quantization.QuantizePosition(obj.PositionXMilli / 1000f),
                QuantizedY = Quantization.QuantizePosition(obj.PositionYMilli / 1000f),
                Health = (ushort)ClampUShort(obj.Health),
                MaxHealth = (ushort)ClampUShort(obj.MaxHealth),
                ObjectiveState = (byte)(obj.Health <= 0 ? CampaignObjectiveStateKind.Completed : CampaignObjectiveStateKind.Active)
            });
        }
    }

    private void AppendHazardSnapshots(WorldSnapshot snapshot)
    {
        var hazardIds = new List<uint>(_activeHazards.Keys);
        hazardIds.Sort();
        for (var i = 0; i < hazardIds.Count; i++)
        {
            if (!_activeHazards.TryGetValue(hazardIds[i], out var hazard) || !hazard.IsActive)
            {
                continue;
            }

            snapshot.Hazards.Add(new WorldHazardSnapshot
            {
                HazardRuntimeId = hazard.RuntimeId,
                HazardId = hazard.HazardId,
                EncounterId = hazard.EncounterId,
                QuantizedX = Quantization.QuantizePosition(hazard.PositionXMilli / 1000f),
                QuantizedY = Quantization.QuantizePosition(hazard.PositionYMilli / 1000f),
                RemainingTicks = (ushort)ClampUShort(hazard.RemainingTicks),
                ObjectiveState = (byte)CampaignObjectiveStateKind.Active
            });
        }
    }

    private void AppendNpcSnapshots(WorldSnapshot snapshot)
    {
        var npcIds = new List<uint>(_activeNpcs.Keys);
        npcIds.Sort();
        for (var i = 0; i < npcIds.Count; i++)
        {
            if (!_activeNpcs.TryGetValue(npcIds[i], out var npc))
            {
                continue;
            }

            snapshot.Npcs.Add(new WorldNpcSnapshot
            {
                NpcRuntimeId = npc.RuntimeId,
                NpcId = npc.NpcId,
                ZoneId = npc.ZoneId,
                Name = npc.Name,
                QuantizedX = Quantization.QuantizePosition(npc.PositionXMilli / 1000f),
                QuantizedY = Quantization.QuantizePosition(npc.PositionYMilli / 1000f),
                InteractRadiusDeciUnits = (ushort)ClampUShort(npc.InteractRadiusMilli / 100),
                ObjectiveState = (byte)CampaignObjectiveStateKind.Active
            });
        }
    }

    private void SpawnFallbackEnemy()
    {
        var entityId = _nextEntityId++;
        var enemy = new SimEntityState
        {
            EntityId = entityId,
            Kind = EntityKind.Enemy,
            PositionXMilli = 4_000,
            PositionYMilli = 0,
            Health = 360
        };

        enemy.Character.Attributes = new CharacterAttributes
        {
            Might = 9,
            Will = 6,
            Alacrity = 8,
            Constitution = 8
        };
        enemy.Character.RecalculateDerivedStats(_statTuning);
        enemy.Health = 360;

        _simState.UpsertEntity(enemy);
        _enemyArchetypeByEntityId[enemy.EntityId] = "enemy.grave_scrabbler";
    }

    private void BootstrapCampaignRuntime()
    {
        var world = _campaignWorldDefinition;
        if (world is null || string.IsNullOrWhiteSpace(world.StartZoneId))
        {
            return;
        }

        if (!world.Zones.TryGetValue(world.StartZoneId, out var startZone))
        {
            return;
        }

        SpawnZoneNpcs(world.StartZoneId);
        SyncActivatedEncounters(startZone.EncounterIds);
    }

    private void ActivateEncounter(string encounterId, int encounterIndex)
    {
        var world = _campaignWorldDefinition;
        if (world is null || !world.Encounters.TryGetValue(encounterId, out var encounter))
        {
            return;
        }
        var encounterLayout = TryResolveEncounterLayout(world, encounter);

        if (!_runtimeObjectIdsByEncounter.ContainsKey(encounterId))
        {
            _runtimeObjectIdsByEncounter[encounterId] = new List<uint>();
        }

        var spawnedAnyObjectsFromLayout = false;
        if (encounterLayout is not null && encounterLayout.ObjectPlacements.Count > 0)
        {
            for (var i = 0; i < encounterLayout.ObjectPlacements.Count; i++)
            {
                var placement = encounterLayout.ObjectPlacements[i];
                if (!world.Objects.TryGetValue(placement.ObjectDefId, out var objectDef))
                {
                    continue;
                }

                var objectAllowed = false;
                for (var j = 0; j < encounter.ObjectiveObjectIds.Count; j++)
                {
                    if (string.Equals(encounter.ObjectiveObjectIds[j], objectDef.Id, StringComparison.Ordinal))
                    {
                        objectAllowed = true;
                        break;
                    }
                }

                if (!objectAllowed)
                {
                    continue;
                }

                var runtimeObject = _simState.SpawnWorldObject(
                    objectDef.Id,
                    placement.XMilli,
                    placement.YMilli,
                    objectDef.MaxHealth,
                    flags: 0,
                    linkedId: 0);
                _objectDefIdByRuntimeId[runtimeObject.ObjectId] = objectDef.Id;
                _objectEncounterIdByRuntimeId[runtimeObject.ObjectId] = encounterId;
                _runtimeObjectIdsByEncounter[encounterId].Add(runtimeObject.ObjectId);
                spawnedAnyObjectsFromLayout = true;

                if (!string.IsNullOrWhiteSpace(objectDef.LinkedHazardId))
                {
                    ActivateHazard(encounterId, objectDef.LinkedHazardId!, runtimeObject.ObjectId, runtimeObject.PositionXMilli, runtimeObject.PositionYMilli);
                }
            }
        }

        if (!spawnedAnyObjectsFromLayout)
        {
            for (var i = 0; i < encounter.ObjectiveObjectIds.Count; i++)
            {
                var objectDefId = encounter.ObjectiveObjectIds[i];
                if (!world.Objects.TryGetValue(objectDefId, out var objectDef))
                {
                    continue;
                }

                var position = ResolveEncounterPosition(encounterId, encounterIndex, i, encounterLayout);
                var runtimeObject = _simState.SpawnWorldObject(
                    objectDef.Id,
                    position.XMilli,
                    position.YMilli,
                    objectDef.MaxHealth,
                    flags: 0,
                    linkedId: 0);
                _objectDefIdByRuntimeId[runtimeObject.ObjectId] = objectDef.Id;
                _objectEncounterIdByRuntimeId[runtimeObject.ObjectId] = encounterId;
                _runtimeObjectIdsByEncounter[encounterId].Add(runtimeObject.ObjectId);

                if (!string.IsNullOrWhiteSpace(objectDef.LinkedHazardId))
                {
                    ActivateHazard(encounterId, objectDef.LinkedHazardId!, runtimeObject.ObjectId, runtimeObject.PositionXMilli, runtimeObject.PositionYMilli);
                }
            }
        }

        var spawnedAnyHazardsFromLayout = false;
        if (encounterLayout is not null && encounterLayout.HazardPlacements.Count > 0)
        {
            for (var i = 0; i < encounterLayout.HazardPlacements.Count; i++)
            {
                var placement = encounterLayout.HazardPlacements[i];
                var hazardAllowed = false;
                for (var j = 0; j < encounter.HazardIds.Count; j++)
                {
                    if (string.Equals(encounter.HazardIds[j], placement.HazardId, StringComparison.Ordinal))
                    {
                        hazardAllowed = true;
                        break;
                    }
                }

                if (!hazardAllowed)
                {
                    continue;
                }

                ActivateHazard(encounterId, placement.HazardId, sourceObjectId: 0, placement.XMilli, placement.YMilli);
                spawnedAnyHazardsFromLayout = true;
            }
        }

        if (!spawnedAnyHazardsFromLayout)
        {
            for (var i = 0; i < encounter.HazardIds.Count; i++)
            {
                var position = ResolveEncounterPosition(encounterId, encounterIndex, encounter.ObjectiveObjectIds.Count + i, encounterLayout);
                ActivateHazard(encounterId, encounter.HazardIds[i], sourceObjectId: 0, position.XMilli, position.YMilli);
            }
        }

        SpawnEncounterEnemies(encounter, encounterId, encounterIndex, encounterLayout);
    }

    private void SpawnEncounterEnemies(
        CampaignEncounterDefinition encounter,
        string encounterId,
        int encounterIndex,
        CampaignEncounterLayoutDefinition? encounterLayout)
    {
        if (encounter.EnemyIds.Count == 0)
        {
            return;
        }

        var count = Math.Min(encounter.EnemyIds.Count, 6);
        for (var i = 0; i < count; i++)
        {
            var position = ResolveEncounterPosition(encounterId, encounterIndex, encounter.ObjectiveObjectIds.Count + encounter.HazardIds.Count + i, encounterLayout);
            var enemy = new SimEntityState
            {
                EntityId = _nextEntityId++,
                Kind = EntityKind.Enemy,
                PositionXMilli = position.XMilli,
                PositionYMilli = position.YMilli,
                Health = 320
            };

            enemy.Character.Attributes = new CharacterAttributes
            {
                Might = 9,
                Will = 6,
                Alacrity = 8,
                Constitution = 8
            };
            enemy.Character.RecalculateDerivedStats(_statTuning);
            enemy.Health = Math.Max(160, enemy.Character.DerivedStats.MaxHealth);

            _simState.UpsertEntity(enemy);
            _enemyArchetypeByEntityId[enemy.EntityId] = encounter.EnemyIds[i];
        }
    }

    private int ResolveEncounterIndex(string encounterId)
    {
        var world = _campaignWorldDefinition;
        if (world is null)
        {
            return 0;
        }

        var allEncounterIds = new List<string>();
        foreach (var zone in world.Zones.Values)
        {
            for (var i = 0; i < zone.EncounterIds.Count; i++)
            {
                allEncounterIds.Add(zone.EncounterIds[i]);
            }
        }

        allEncounterIds.Sort(StringComparer.Ordinal);
        for (var i = 0; i < allEncounterIds.Count; i++)
        {
            if (string.Equals(allEncounterIds[i], encounterId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    private void SpawnZoneNpcs(string zoneId)
    {
        var world = _campaignWorldDefinition;
        if (world is null || string.IsNullOrWhiteSpace(zoneId))
        {
            return;
        }

        if (!world.ZoneLayouts.TryGetValue(zoneId, out var zoneLayout))
        {
            return;
        }

        for (var i = 0; i < zoneLayout.NpcPlacements.Count; i++)
        {
            var placement = zoneLayout.NpcPlacements[i];
            if (_spawnedNpcIds.Contains(placement.NpcId) || !world.Npcs.TryGetValue(placement.NpcId, out var npcDef))
            {
                continue;
            }

            var runtime = new CampaignNpcRuntimeState
            {
                RuntimeId = _nextNpcRuntimeId++,
                NpcId = npcDef.Id,
                ZoneId = npcDef.ZoneId,
                Name = npcDef.Name,
                PositionXMilli = placement.XMilli,
                PositionYMilli = placement.YMilli,
                InteractRadiusMilli = Math.Max(300, npcDef.InteractRadiusMilli)
            };

            _activeNpcs[runtime.RuntimeId] = runtime;
            _npcIdByRuntimeNpcId[runtime.RuntimeId] = runtime.NpcId;
            _spawnedNpcIds.Add(runtime.NpcId);
        }
    }

    private void ActivateHazard(string encounterId, string hazardId, uint sourceObjectId, int xMilli, int yMilli)
    {
        var world = _campaignWorldDefinition;
        if (world is null || !world.Hazards.TryGetValue(hazardId, out var definition))
        {
            return;
        }

        var key = sourceObjectId == 0 ? $"{encounterId}:{hazardId}" : $"{encounterId}:{hazardId}:{sourceObjectId}";
        foreach (var existing in _activeHazards.Values)
        {
            if (string.Equals(existing.IdentityKey, key, StringComparison.Ordinal))
            {
                existing.RemainingTicks = Math.Max(existing.RemainingTicks, definition.DurationTicks);
                return;
            }
        }

        var state = new CampaignHazardRuntimeState
        {
            RuntimeId = _nextHazardRuntimeId++,
            IdentityKey = key,
            EncounterId = encounterId,
            HazardId = hazardId,
            SourceObjectId = sourceObjectId,
            PositionXMilli = xMilli,
            PositionYMilli = yMilli,
            RemainingTicks = Math.Max(1, definition.DurationTicks),
            TickIntervalTicks = Math.Max(1, definition.TickIntervalTicks),
            TickCountdownTicks = 1,
            EffectTags = new List<string>(definition.EffectTags),
            IsActive = true
        };

        _activeHazards[state.RuntimeId] = state;
    }

    private void TickCampaignObjectInteractions()
    {
        if (_simState.WorldObjects.Count == 0)
        {
            return;
        }

        var playerIds = new List<uint>();
        foreach (var entity in _simState.Entities.Values)
        {
            if (entity.Kind == EntityKind.Player && entity.IsAlive)
            {
                playerIds.Add(entity.EntityId);
            }
        }

        playerIds.Sort();
        for (var i = 0; i < playerIds.Count; i++)
        {
            if (!_simState.TryGetEntity(playerIds[i], out var player))
            {
                continue;
            }

            var canAttack = (player.ActionFlags & (InputActionFlags.FastAttackHold | InputActionFlags.HeavyAttackHold)) != 0;
            var canInteract = (player.ActionFlags & (InputActionFlags.Interact | InputActionFlags.Pickup)) != 0;
            if (!canAttack && !canInteract)
            {
                continue;
            }

            var target = ResolveNearestInteractableObject(player, canAttack, canInteract);
            if (target is null || target.Health <= 0)
            {
                continue;
            }

            var damage = canAttack
                ? ((player.ActionFlags & InputActionFlags.HeavyAttackHold) != 0 ? 18 : 8)
                : target.Health;

            target.Health = Math.Max(0, target.Health - damage);
            if (target.Health > 0)
            {
                continue;
            }

            _simState.RecordSimEvent(new SimEventRecord
            {
                Kind = SimEventKind.ObjectDestroyed,
                Tick = _simState.Tick,
                PlayerEntityId = player.EntityId,
                SubjectObjectId = target.ObjectId,
                Value = 1
            });

            if (_objectEncounterIdByRuntimeId.TryGetValue(target.ObjectId, out var encounterId) &&
                _runtimeObjectIdsByEncounter.TryGetValue(encounterId, out var objectIds))
            {
                var allDestroyed = true;
                for (var j = 0; j < objectIds.Count; j++)
                {
                    if (_simState.TryGetWorldObject(objectIds[j], out var existing) && existing.Health > 0)
                    {
                        allDestroyed = false;
                        break;
                    }
                }

                if (allDestroyed)
                {
                    _simState.RecordSimEvent(new SimEventRecord
                    {
                        Kind = SimEventKind.ObjectiveCompleted,
                        Tick = _simState.Tick,
                        PlayerEntityId = player.EntityId,
                        SubjectObjectId = target.ObjectId,
                        Value = 1
                    });
                }
            }

            DeactivateHazardsForSourceObject(target.ObjectId);
        }
    }

    private void TickCampaignNpcInteractions()
    {
        if (_activeNpcs.Count == 0)
        {
            return;
        }

        var playerIds = new List<uint>();
        foreach (var entity in _simState.Entities.Values)
        {
            if (entity.Kind == EntityKind.Player && entity.IsAlive)
            {
                playerIds.Add(entity.EntityId);
            }
        }

        playerIds.Sort();
        for (var i = 0; i < playerIds.Count; i++)
        {
            if (!_simState.TryGetEntity(playerIds[i], out var player))
            {
                continue;
            }

            var canInteract = (player.ActionFlags & (InputActionFlags.Interact | InputActionFlags.Pickup)) != 0;
            if (!canInteract)
            {
                continue;
            }

            var npc = ResolveNearestNpc(player);
            if (npc is null)
            {
                continue;
            }

            var pairKey = ((ulong)player.EntityId << 32) | npc.RuntimeId;
            if (_lastNpcInteractTickByPair.TryGetValue(pairKey, out var lastTick) &&
                _simState.Tick - lastTick < (uint)Math.Max(1, _simRules.SimulationHz / 2))
            {
                continue;
            }

            _lastNpcInteractTickByPair[pairKey] = _simState.Tick;
            _simState.RecordSimEvent(new SimEventRecord
            {
                Kind = SimEventKind.NpcInteracted,
                Tick = _simState.Tick,
                PlayerEntityId = player.EntityId,
                SubjectEntityId = npc.RuntimeId,
                Value = 1
            });
        }
    }

    private SimWorldObjectState? ResolveNearestInteractableObject(SimEntityState player, bool canAttack, bool canInteract)
    {
        SimWorldObjectState? nearest = null;
        long nearestDistSq = long.MaxValue;
        foreach (var obj in _simState.WorldObjects.Values)
        {
            if (obj.Health <= 0 || !_objectDefIdByRuntimeId.TryGetValue(obj.ObjectId, out var defId))
            {
                continue;
            }

            var mode = _campaignWorldDefinition?.Objects.TryGetValue(defId, out var def) == true
                ? def.InteractMode
                : "attack_to_destroy";
            if (mode.Equals("attack_to_destroy", StringComparison.OrdinalIgnoreCase) && !canAttack)
            {
                continue;
            }

            if (mode.Equals("interact_to_disable", StringComparison.OrdinalIgnoreCase) && !canInteract)
            {
                continue;
            }

            var dx = obj.PositionXMilli - player.PositionXMilli;
            var dy = obj.PositionYMilli - player.PositionYMilli;
            var distSq = (long)dx * dx + (long)dy * dy;
            if (distSq > (long)2_000 * 2_000 || distSq >= nearestDistSq)
            {
                continue;
            }

            nearest = obj;
            nearestDistSq = distSq;
        }

        return nearest;
    }

    private CampaignNpcRuntimeState? ResolveNearestNpc(SimEntityState player)
    {
        CampaignNpcRuntimeState? nearest = null;
        long nearestDistSq = long.MaxValue;
        foreach (var npc in _activeNpcs.Values)
        {
            var dx = npc.PositionXMilli - player.PositionXMilli;
            var dy = npc.PositionYMilli - player.PositionYMilli;
            var distSq = (long)dx * dx + (long)dy * dy;
            var radius = Math.Max(300, npc.InteractRadiusMilli);
            if (distSq > (long)radius * radius || distSq >= nearestDistSq)
            {
                continue;
            }

            nearest = npc;
            nearestDistSq = distSq;
        }

        return nearest;
    }

    private void TickCampaignHazards()
    {
        if (_activeHazards.Count == 0)
        {
            return;
        }

        var hazardIds = new List<uint>(_activeHazards.Keys);
        hazardIds.Sort();
        for (var i = 0; i < hazardIds.Count; i++)
        {
            if (!_activeHazards.TryGetValue(hazardIds[i], out var hazard) || !hazard.IsActive)
            {
                continue;
            }

            if (hazard.SourceObjectId != 0 && _simState.TryGetWorldObject(hazard.SourceObjectId, out var sourceObject))
            {
                hazard.PositionXMilli = sourceObject.PositionXMilli;
                hazard.PositionYMilli = sourceObject.PositionYMilli;
            }

            hazard.RemainingTicks--;
            hazard.TickCountdownTicks--;
            if (hazard.TickCountdownTicks <= 0)
            {
                PulseHazard(hazard);
                hazard.TickCountdownTicks = Math.Max(1, hazard.TickIntervalTicks);
            }

            if (hazard.RemainingTicks <= 0)
            {
                hazard.IsActive = false;
            }
        }

        foreach (var id in hazardIds)
        {
            if (_activeHazards.TryGetValue(id, out var hazard) && !hazard.IsActive)
            {
                _activeHazards.Remove(id);
            }
        }
    }

    private void PulseHazard(CampaignHazardRuntimeState hazard)
    {
        const int hazardRadiusMilli = 1800;
        var radiusSq = (long)hazardRadiusMilli * hazardRadiusMilli;

        foreach (var entity in _simState.Entities.Values)
        {
            if (!entity.IsAlive)
            {
                continue;
            }

            var dx = entity.PositionXMilli - hazard.PositionXMilli;
            var dy = entity.PositionYMilli - hazard.PositionYMilli;
            var distSq = (long)dx * dx + (long)dy * dy;
            if (distSq > radiusSq)
            {
                continue;
            }

            if (ContainsEffectTag(hazard.EffectTags, "damage"))
            {
                var damage = entity.Kind == EntityKind.Player ? 5 : 2;
                entity.Health = Math.Max(0, entity.Health - damage);
            }

            if (ContainsEffectTag(hazard.EffectTags, "enemy_buff") && entity.Kind == EntityKind.Enemy)
            {
                entity.Health += 1;
            }

            if (ContainsEffectTag(hazard.EffectTags, "threat_amp") && entity.Kind == EntityKind.Enemy)
            {
                var keys = new List<uint>(entity.ThreatByPlayerEntityId.Keys);
                for (var i = 0; i < keys.Count; i++)
                {
                    entity.ThreatByPlayerEntityId[keys[i]] = Math.Min(200_000, entity.ThreatByPlayerEntityId[keys[i]] + 10);
                }
            }

            if (ContainsEffectTag(hazard.EffectTags, "slow") && entity.Kind == EntityKind.Player)
            {
                entity.Statuses["status.hazard.slow"] = new SimStatusState
                {
                    Id = "status.hazard.slow",
                    Stacks = 1,
                    RemainingTicks = Math.Max(1, _simRules.SimulationHz / 2)
                };
            }
        }
    }

    private static bool ContainsEffectTag(IReadOnlyList<string> tags, string tag)
    {
        for (var i = 0; i < tags.Count; i++)
        {
            if (tags[i].Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void DeactivateHazardsForSourceObject(uint sourceObjectId)
    {
        if (sourceObjectId == 0 || _activeHazards.Count == 0)
        {
            return;
        }

        var ids = new List<uint>(_activeHazards.Keys);
        for (var i = 0; i < ids.Count; i++)
        {
            if (_activeHazards.TryGetValue(ids[i], out var hazard) && hazard.SourceObjectId == sourceObjectId)
            {
                _activeHazards.Remove(ids[i]);
            }
        }
    }

    private static (int XMilli, int YMilli) ResolveEncounterPosition(
        string encounterId,
        int encounterIndex,
        int offset,
        CampaignEncounterLayoutDefinition? layout)
    {
        if (layout is { AnchorXMilli: not null, AnchorYMilli: not null })
        {
            var ring = 800;
            var angle = ((offset % 8) / 8.0) * Math.PI * 2.0;
            var x = layout.AnchorXMilli.Value + (int)Math.Round(Math.Cos(angle) * ring, MidpointRounding.AwayFromZero);
            var y = layout.AnchorYMilli.Value + (int)Math.Round(Math.Sin(angle) * ring, MidpointRounding.AwayFromZero);
            return (x, y);
        }

        unchecked
        {
            var hash = 17;
            for (var i = 0; i < encounterId.Length; i++)
            {
                hash = hash * 31 + encounterId[i];
            }

            hash = hash * 31 + encounterIndex;
            hash = hash * 31 + offset;
            var x = (hash % 14_000) - 7_000;
            var y = ((hash / 97) % 10_000) - 5_000;
            return (x, y);
        }
    }

    private static CampaignEncounterLayoutDefinition? TryResolveEncounterLayout(
        CampaignWorldDefinition world,
        CampaignEncounterDefinition encounter)
    {
        if (string.IsNullOrWhiteSpace(encounter.ZoneId))
        {
            return null;
        }

        if (!world.ZoneLayouts.TryGetValue(encounter.ZoneId, out var zoneLayout))
        {
            return null;
        }

        return zoneLayout.Encounters.TryGetValue(encounter.Id, out var encounterLayout)
            ? encounterLayout
            : null;
    }

    private static string ResolveObjectArchetype(string objectDefId)
    {
        if (objectDefId.Contains("corpse_pile", StringComparison.Ordinal)) return "spawner";
        if (objectDefId.Contains("supply", StringComparison.Ordinal)) return "interactable";
        if (objectDefId.Contains("pylon", StringComparison.Ordinal) || objectDefId.Contains("anchor", StringComparison.Ordinal) || objectDefId.Contains("node", StringComparison.Ordinal)) return "objective";
        return "destructible";
    }

    private static PlayerTransferState BuildTransferState(SimEntityState entity)
    {
        return new PlayerTransferState
        {
            CharacterId = entity.Character.CharacterId,
            Attributes = entity.Character.Attributes,
            Level = entity.Character.Level,
            Experience = entity.Character.Experience,
            Currency = entity.Character.Currency,
            Health = entity.Health,
            BuilderResource = entity.BuilderResource,
            SpenderResource = entity.SpenderResource,
            AbilityProfileId = entity.AbilityProfileId
        };
    }

    private void ApplyTransferState(SimEntityState entity, in PlayerTransferState transfer)
    {
        entity.Character.CharacterId = transfer.CharacterId;
        entity.Character.Attributes = transfer.Attributes;
        entity.Character.Level = transfer.Level;
        entity.Character.Experience = transfer.Experience;
        entity.Character.Currency = transfer.Currency;
        entity.Character.RecalculateDerivedStats(_statTuning);

        entity.Health = transfer.Health > 0 ? transfer.Health : entity.Character.DerivedStats.MaxHealth;
        entity.BuilderResource = transfer.BuilderResource;
        entity.SpenderResource = transfer.SpenderResource;
    }

    private static int ClampUShort(int value)
    {
        return value < 0 ? 0 : (value > ushort.MaxValue ? ushort.MaxValue : value);
    }

    private static byte ClampByte(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        return value > byte.MaxValue ? byte.MaxValue : (byte)value;
    }

    private string ResolveEntityArchetypeId(SimEntityState entity)
    {
        if (entity.Kind != EntityKind.Enemy)
        {
            return string.Empty;
        }

        return _enemyArchetypeByEntityId.TryGetValue(entity.EntityId, out var archetypeId)
            ? archetypeId
            : "enemy.unknown";
    }

    private static uint ResolveAggroTarget(SimEntityState entity)
    {
        if (entity.Kind != EntityKind.Enemy)
        {
            return 0;
        }

        if (entity.ForcedTargetTicks > 0 && entity.ForcedTargetEntityId != 0)
        {
            return entity.ForcedTargetEntityId;
        }

        var bestThreat = int.MinValue;
        var bestTarget = 0u;
        foreach (var kvp in entity.ThreatByPlayerEntityId)
        {
            if (kvp.Value > bestThreat)
            {
                bestThreat = kvp.Value;
                bestTarget = kvp.Key;
            }
        }

        return bestTarget;
    }

    private static ushort ResolveAggroThreat(SimEntityState entity)
    {
        if (entity.Kind != EntityKind.Enemy || entity.ThreatByPlayerEntityId.Count == 0)
        {
            return 0;
        }

        var bestThreat = 0;
        foreach (var kvp in entity.ThreatByPlayerEntityId)
        {
            if (kvp.Value > bestThreat)
            {
                bestThreat = kvp.Value;
            }
        }

        return (ushort)Math.Clamp(bestThreat, 0, ushort.MaxValue);
    }

    private static byte ResolvePrimaryStatusStacks(SimEntityState entity)
    {
        var maxStacks = 0;
        foreach (var status in entity.Statuses.Values)
        {
            if (status.Stacks <= 0)
            {
                continue;
            }

            if (status.Stacks > maxStacks)
            {
                maxStacks = status.Stacks;
            }
        }

        return ClampByte(maxStacks);
    }

    private string ResolveAbilityProfileId(string? requestedProfileId)
    {
        if (!string.IsNullOrWhiteSpace(requestedProfileId) && _simState.HasAbilityProfile(requestedProfileId))
        {
            return requestedProfileId;
        }

        return _fallbackAbilityProfileId;
    }

    private sealed class CampaignHazardRuntimeState
    {
        public uint RuntimeId { get; set; }
        public string IdentityKey { get; set; } = string.Empty;
        public string EncounterId { get; set; } = string.Empty;
        public string HazardId { get; set; } = string.Empty;
        public uint SourceObjectId { get; set; }
        public int PositionXMilli { get; set; }
        public int PositionYMilli { get; set; }
        public int TickIntervalTicks { get; set; }
        public int TickCountdownTicks { get; set; }
        public int RemainingTicks { get; set; }
        public List<string> EffectTags { get; set; } = new();
        public bool IsActive { get; set; }
    }

    private sealed class CampaignNpcRuntimeState
    {
        public uint RuntimeId { get; set; }
        public string NpcId { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int PositionXMilli { get; set; }
        public int PositionYMilli { get; set; }
        public int InteractRadiusMilli { get; set; } = 2000;
    }
}

public struct PlayerTransferState
{
    public uint CharacterId;
    public CharacterAttributes Attributes;
    public ushort Level;
    public uint Experience;
    public int Currency;
    public int Health;
    public int BuilderResource;
    public int SpenderResource;
    public string AbilityProfileId;
}
