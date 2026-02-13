#nullable enable
using System;
using System.Collections.Generic;
using Armament.SharedSim.Determinism;
using Armament.SharedSim.Protocol;

namespace Armament.SharedSim.Sim;

public sealed class SimEntityState
{
    public uint EntityId { get; set; }
    public EntityKind Kind { get; set; }
    public bool IsAlive { get; set; } = true;

    public int PositionXMilli { get; set; }
    public int PositionYMilli { get; set; }

    public short InputX { get; set; }
    public short InputY { get; set; }
    public InputActionFlags ActionFlags { get; set; }

    public CharacterContext Character { get; } = new();

    public int Health { get; set; }
    public int ShieldMilli { get; set; }
    public int DamageReductionPermille { get; set; }
    public int DamageReductionTicks { get; set; }
    public int BuilderResource { get; set; }
    public int SpenderResource { get; set; }
    public Dictionary<string, SimStatusState> Statuses { get; } = new(StringComparer.Ordinal);

    public int FastAttackCooldownTicks { get; set; }
    public int HeavyAttackCooldownTicks { get; set; }
    public int EnemyAttackCooldownTicks { get; set; }
    public uint ForcedTargetEntityId { get; set; }
    public int ForcedTargetTicks { get; set; }
    public int DebugLastConsumedStatusStacks { get; set; }
    public int DebugLastConsumedStatusTicks { get; set; }
    public int DebugLastCastFeedbackTicks { get; set; }
    public int DebugLastCastSlotCode { get; set; }
    public int DebugLastCastResultCode { get; set; }
    public int DebugLastCastTargetTeamCode { get; set; }
    public int DebugLastCastAffectedCount { get; set; }
    public int DebugLastCastVfxCode { get; set; }
    public Dictionary<uint, int> ThreatByPlayerEntityId { get; } = new();
    public int[] SkillCooldownTicks { get; } = new int[8];
    public string AbilityProfileId { get; set; } = SimAbilityProfiles.BuiltinV1.Id;
}

public sealed class SimStatusState
{
    public string Id { get; set; } = string.Empty;
    public int Stacks { get; set; }
    public int RemainingTicks { get; set; }
}

public sealed class SimZoneState
{
    public uint ZoneId { get; set; }
    public string ZoneDefId { get; set; } = string.Empty;
    public uint OwnerEntityId { get; set; }
    public int PositionXMilli { get; set; }
    public int PositionYMilli { get; set; }
    public int RadiusMilli { get; set; }
    public int DamagePerPulse { get; set; }
    public int HealPerPulse { get; set; }
    public string? StatusId { get; set; }
    public int StatusDurationTicks { get; set; }
    public int TickIntervalTicks { get; set; }
    public int TickCountdownTicks { get; set; }
    public int RemainingTicks { get; set; }
}

public sealed class SimLinkState
{
    public uint LinkId { get; set; }
    public string LinkDefId { get; set; } = string.Empty;
    public uint OwnerEntityId { get; set; }
    public uint TargetEntityId { get; set; }
    public int RemainingTicks { get; set; }
    public int MaxDistanceMilli { get; set; }
    public int PullMilliPerTick { get; set; }
    public int DamagePerTick { get; set; }
}

public sealed class SimLootDrop
{
    public uint LootId { get; set; }
    public int PositionXMilli { get; set; }
    public int PositionYMilli { get; set; }
    public int CurrencyAmount { get; set; }
    public bool AutoLoot { get; set; } = true;
    public bool Claimed { get; set; }
}

public enum SimEventKind : byte
{
    EnemyKilled = 1,
    ObjectDestroyed = 2,
    ObjectiveCompleted = 3,
    TokenCollected = 4,
    PlayerEnteredRegion = 5,
    NpcInteracted = 6
}

public struct SimEventRecord
{
    public SimEventKind Kind { get; set; }
    public uint Tick { get; set; }
    public uint PlayerEntityId { get; set; }
    public uint SubjectEntityId { get; set; }
    public uint SubjectObjectId { get; set; }
    public int Value { get; set; }
}

public struct SimLootGrantEvent
{
    public uint PlayerEntityId { get; set; }
    public uint LootId { get; set; }
    public int CurrencyAmount { get; set; }
    public bool AutoLoot { get; set; }
}

public sealed class SimWorldObjectState
{
    public uint ObjectId { get; set; }
    public string ObjectDefId { get; set; } = string.Empty;
    public int PositionXMilli { get; set; }
    public int PositionYMilli { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public uint Flags { get; set; }
    public uint LinkedId { get; set; }
}

public sealed class OverworldSimState
{
    private readonly Dictionary<uint, SimEntityState> _entities = new();
    private readonly Dictionary<string, SimAbilityProfile> _abilityProfiles = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, SimLootDrop> _lootDrops = new();
    private readonly Dictionary<uint, SimZoneState> _zones = new();
    private readonly Dictionary<uint, SimLinkState> _links = new();
    private readonly Dictionary<uint, SimWorldObjectState> _worldObjects = new();
    private readonly Dictionary<string, SimZoneDefinition> _zoneDefinitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SimLinkDefinition> _linkDefinitions = new(StringComparer.Ordinal);
    private readonly List<SimLootGrantEvent> _lootGrantEvents = new();
    private readonly List<SimEventRecord> _simEvents = new();
    private uint _nextLootId = 1_000_000;
    private uint _nextZoneId = 1;
    private uint _nextLinkId = 1;
    private uint _nextObjectId = 2_000_000;

    public uint Tick { get; set; }
    public uint Seed { get; set; } = 1;
    public string DefaultAbilityProfileId { get; set; } = SimAbilityProfiles.BuiltinV1.Id;
    public IReadOnlyDictionary<string, SimAbilityProfile> AbilityProfiles => _abilityProfiles;
    public SimAbilityProfile AbilityProfile
    {
        get => ResolveAbilityProfile(DefaultAbilityProfileId);
        set
        {
            RegisterAbilityProfile(value);
            DefaultAbilityProfileId = value.Id;
        }
    }
    public IReadOnlyDictionary<uint, SimEntityState> Entities => _entities;
    public IReadOnlyDictionary<uint, SimLootDrop> LootDrops => _lootDrops;
    public IReadOnlyDictionary<uint, SimZoneState> Zones => _zones;
    public IReadOnlyDictionary<uint, SimLinkState> Links => _links;
    public IReadOnlyDictionary<uint, SimWorldObjectState> WorldObjects => _worldObjects;
    public IReadOnlyDictionary<string, SimZoneDefinition> ZoneDefinitions => _zoneDefinitions;
    public IReadOnlyDictionary<string, SimLinkDefinition> LinkDefinitions => _linkDefinitions;
    public IReadOnlyList<SimLootGrantEvent> LootGrantEvents => _lootGrantEvents;
    public IReadOnlyList<SimEventRecord> SimEvents => _simEvents;

    public OverworldSimState()
    {
        RegisterAbilityProfile(SimAbilityProfiles.BuiltinV1);
        foreach (var zone in SimZoneLinkDefaults.Zones)
        {
            RegisterZoneDefinition(zone);
        }

        foreach (var link in SimZoneLinkDefaults.Links)
        {
            RegisterLinkDefinition(link);
        }
    }

    public void UpsertEntity(SimEntityState entity)
    {
        _entities[entity.EntityId] = entity;
    }

    public bool TryGetEntity(uint entityId, out SimEntityState entity)
    {
        return _entities.TryGetValue(entityId, out entity!);
    }

    public bool RemoveEntity(uint entityId)
    {
        return _entities.Remove(entityId);
    }

    public void RegisterAbilityProfile(SimAbilityProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            return;
        }

        _abilityProfiles[profile.Id] = profile;
    }

    public bool HasAbilityProfile(string profileId)
    {
        return !string.IsNullOrWhiteSpace(profileId) && _abilityProfiles.ContainsKey(profileId);
    }

    public SimAbilityProfile ResolveAbilityProfile(string? profileId)
    {
        if (!string.IsNullOrWhiteSpace(profileId) && _abilityProfiles.TryGetValue(profileId, out var profile))
        {
            return profile;
        }

        if (_abilityProfiles.TryGetValue(DefaultAbilityProfileId, out profile))
        {
            return profile;
        }

        return SimAbilityProfiles.BuiltinV1;
    }

    public SimLootDrop SpawnLoot(int xMilli, int yMilli, int currencyAmount, bool autoLoot = true)
    {
        var loot = new SimLootDrop
        {
            LootId = _nextLootId++,
            PositionXMilli = xMilli,
            PositionYMilli = yMilli,
            CurrencyAmount = currencyAmount,
            AutoLoot = autoLoot
        };
        _lootDrops[loot.LootId] = loot;
        return loot;
    }

    public SimZoneState SpawnZone(
        string zoneDefId,
        uint ownerEntityId,
        int xMilli,
        int yMilli,
        int durationTicks,
        int tickIntervalTicks,
        int radiusMilli,
        int damagePerPulse,
        int healPerPulse,
        string? statusId,
        int statusDurationTicks)
    {
        var zone = new SimZoneState
        {
            ZoneId = _nextZoneId++,
            ZoneDefId = zoneDefId,
            OwnerEntityId = ownerEntityId,
            PositionXMilli = xMilli,
            PositionYMilli = yMilli,
            RemainingTicks = Math.Max(1, durationTicks),
            TickIntervalTicks = Math.Max(1, tickIntervalTicks),
            TickCountdownTicks = 1,
            RadiusMilli = Math.Max(200, radiusMilli),
            DamagePerPulse = Math.Max(0, damagePerPulse),
            HealPerPulse = Math.Max(0, healPerPulse),
            StatusId = statusId,
            StatusDurationTicks = Math.Max(1, statusDurationTicks)
        };

        _zones[zone.ZoneId] = zone;
        return zone;
    }

    public void ClearStepEvents()
    {
        _lootGrantEvents.Clear();
        _simEvents.Clear();
    }

    public void RecordLootGrant(uint playerEntityId, uint lootId, int currencyAmount, bool autoLoot)
    {
        _lootGrantEvents.Add(new SimLootGrantEvent
        {
            PlayerEntityId = playerEntityId,
            LootId = lootId,
            CurrencyAmount = currencyAmount,
            AutoLoot = autoLoot
        });
    }

    public void RecordSimEvent(SimEventRecord simEvent)
    {
        _simEvents.Add(simEvent);
    }

    public bool RemoveZone(uint zoneId)
    {
        return _zones.Remove(zoneId);
    }

    public SimLinkState SpawnLink(
        string linkDefId,
        uint ownerEntityId,
        uint targetEntityId,
        int durationTicks,
        int maxDistanceMilli,
        int pullMilliPerTick,
        int damagePerTick)
    {
        var link = new SimLinkState
        {
            LinkId = _nextLinkId++,
            LinkDefId = linkDefId,
            OwnerEntityId = ownerEntityId,
            TargetEntityId = targetEntityId,
            RemainingTicks = Math.Max(1, durationTicks),
            MaxDistanceMilli = Math.Max(500, maxDistanceMilli),
            PullMilliPerTick = Math.Max(0, pullMilliPerTick),
            DamagePerTick = Math.Max(0, damagePerTick)
        };

        _links[link.LinkId] = link;
        return link;
    }

    public bool RemoveLink(uint linkId)
    {
        return _links.Remove(linkId);
    }

    public SimWorldObjectState SpawnWorldObject(
        string objectDefId,
        int xMilli,
        int yMilli,
        int maxHealth,
        uint flags,
        uint linkedId = 0)
    {
        var obj = new SimWorldObjectState
        {
            ObjectId = _nextObjectId++,
            ObjectDefId = objectDefId,
            PositionXMilli = xMilli,
            PositionYMilli = yMilli,
            MaxHealth = Math.Max(1, maxHealth),
            Health = Math.Max(1, maxHealth),
            Flags = flags,
            LinkedId = linkedId
        };

        _worldObjects[obj.ObjectId] = obj;
        return obj;
    }

    public bool TryGetWorldObject(uint objectId, out SimWorldObjectState obj)
    {
        return _worldObjects.TryGetValue(objectId, out obj!);
    }

    public bool RemoveWorldObject(uint objectId)
    {
        return _worldObjects.Remove(objectId);
    }

    public void RegisterZoneDefinition(SimZoneDefinition definition)
    {
        if (definition is null || string.IsNullOrWhiteSpace(definition.Id))
        {
            return;
        }

        _zoneDefinitions[definition.Id] = definition;
    }

    public void RegisterLinkDefinition(SimLinkDefinition definition)
    {
        if (definition is null || string.IsNullOrWhiteSpace(definition.Id))
        {
            return;
        }

        _linkDefinitions[definition.Id] = definition;
    }

    public bool TryResolveZoneDefinition(string zoneDefId, out SimZoneDefinition definition)
    {
        return _zoneDefinitions.TryGetValue(zoneDefId, out definition!);
    }

    public bool TryResolveLinkDefinition(string linkDefId, out SimLinkDefinition definition)
    {
        return _linkDefinitions.TryGetValue(linkDefId, out definition!);
    }

    public uint ComputeWorldHash()
    {
        var values = new List<uint>(_entities.Count * 12 + _lootDrops.Count * 5 + _zones.Count * 11 + _links.Count * 9 + _worldObjects.Count * 8 + 6)
        {
            Tick,
            (uint)_entities.Count,
            (uint)_lootDrops.Count,
            (uint)_zones.Count,
            (uint)_links.Count,
            (uint)_worldObjects.Count,
            Seed
        };

        var ids = new List<uint>(_entities.Keys);
        ids.Sort();

        foreach (var id in ids)
        {
            var entity = _entities[id];
            values.Add(entity.EntityId);
            values.Add((uint)entity.Kind);
            values.Add(entity.IsAlive ? 1u : 0u);
            values.Add(unchecked((uint)entity.PositionXMilli));
            values.Add(unchecked((uint)entity.PositionYMilli));
            values.Add(unchecked((uint)entity.InputX));
            values.Add(unchecked((uint)entity.InputY));
            values.Add((uint)entity.ActionFlags);
            values.Add(unchecked((uint)entity.Health));
            values.Add(unchecked((uint)entity.ShieldMilli));
            values.Add(unchecked((uint)entity.DamageReductionPermille));
            values.Add(unchecked((uint)entity.DamageReductionTicks));
            values.Add(entity.ForcedTargetEntityId);
            values.Add(unchecked((uint)entity.ForcedTargetTicks));
            values.Add(unchecked((uint)entity.DebugLastConsumedStatusStacks));
            values.Add(unchecked((uint)entity.DebugLastConsumedStatusTicks));
            values.Add(unchecked((uint)entity.DebugLastCastFeedbackTicks));
            values.Add(unchecked((uint)entity.DebugLastCastSlotCode));
            values.Add(unchecked((uint)entity.DebugLastCastResultCode));
            values.Add(unchecked((uint)entity.DebugLastCastTargetTeamCode));
            values.Add(unchecked((uint)entity.DebugLastCastAffectedCount));
            values.Add(unchecked((uint)entity.DebugLastCastVfxCode));
            values.Add(unchecked((uint)entity.BuilderResource));
            values.Add(unchecked((uint)entity.SpenderResource));
            values.Add(unchecked((uint)entity.Character.Currency));
            values.Add((uint)entity.Statuses.Count);
            var statusIds = new List<string>(entity.Statuses.Keys);
            statusIds.Sort(StringComparer.Ordinal);
            for (var i = 0; i < statusIds.Count; i++)
            {
                var status = entity.Statuses[statusIds[i]];
                values.Add((uint)status.Id.Length);
                for (var c = 0; c < status.Id.Length; c++)
                {
                    values.Add(status.Id[c]);
                }

                values.Add(unchecked((uint)status.Stacks));
                values.Add(unchecked((uint)status.RemainingTicks));
            }
            values.Add((uint)entity.ThreatByPlayerEntityId.Count);
            var threatKeys = new List<uint>(entity.ThreatByPlayerEntityId.Keys);
            threatKeys.Sort();
            for (var i = 0; i < threatKeys.Count; i++)
            {
                var key = threatKeys[i];
                values.Add(key);
                values.Add(unchecked((uint)entity.ThreatByPlayerEntityId[key]));
            }
            values.Add((uint)entity.AbilityProfileId.Length);
            for (var i = 0; i < entity.AbilityProfileId.Length; i++)
            {
                values.Add(entity.AbilityProfileId[i]);
            }
        }

        var lootIds = new List<uint>(_lootDrops.Keys);
        lootIds.Sort();
        foreach (var lootId in lootIds)
        {
            var loot = _lootDrops[lootId];
            values.Add(lootId);
            values.Add(unchecked((uint)loot.PositionXMilli));
            values.Add(unchecked((uint)loot.PositionYMilli));
            values.Add(unchecked((uint)loot.CurrencyAmount));
            values.Add(loot.Claimed ? 1u : 0u);
        }

        var zoneIds = new List<uint>(_zones.Keys);
        zoneIds.Sort();
        foreach (var zoneId in zoneIds)
        {
            var zone = _zones[zoneId];
            values.Add(zone.ZoneId);
            values.Add((uint)zone.ZoneDefId.Length);
            for (var i = 0; i < zone.ZoneDefId.Length; i++)
            {
                values.Add(zone.ZoneDefId[i]);
            }

            values.Add(zone.OwnerEntityId);
            values.Add(unchecked((uint)zone.PositionXMilli));
            values.Add(unchecked((uint)zone.PositionYMilli));
            values.Add(unchecked((uint)zone.RadiusMilli));
            values.Add(unchecked((uint)zone.DamagePerPulse));
            values.Add(unchecked((uint)zone.HealPerPulse));
            values.Add(unchecked((uint)zone.TickIntervalTicks));
            values.Add(unchecked((uint)zone.TickCountdownTicks));
            values.Add(unchecked((uint)zone.RemainingTicks));
        }

        var linkIds = new List<uint>(_links.Keys);
        linkIds.Sort();
        foreach (var linkId in linkIds)
        {
            var link = _links[linkId];
            values.Add(link.LinkId);
            values.Add((uint)link.LinkDefId.Length);
            for (var i = 0; i < link.LinkDefId.Length; i++)
            {
                values.Add(link.LinkDefId[i]);
            }

            values.Add(link.OwnerEntityId);
            values.Add(link.TargetEntityId);
            values.Add(unchecked((uint)link.RemainingTicks));
            values.Add(unchecked((uint)link.MaxDistanceMilli));
            values.Add(unchecked((uint)link.PullMilliPerTick));
            values.Add(unchecked((uint)link.DamagePerTick));
        }

        var objectIds = new List<uint>(_worldObjects.Keys);
        objectIds.Sort();
        foreach (var objectId in objectIds)
        {
            var obj = _worldObjects[objectId];
            values.Add(obj.ObjectId);
            values.Add((uint)obj.ObjectDefId.Length);
            for (var i = 0; i < obj.ObjectDefId.Length; i++)
            {
                values.Add(obj.ObjectDefId[i]);
            }

            values.Add(unchecked((uint)obj.PositionXMilli));
            values.Add(unchecked((uint)obj.PositionYMilli));
            values.Add(unchecked((uint)obj.Health));
            values.Add(unchecked((uint)obj.MaxHealth));
            values.Add(obj.Flags);
            values.Add(obj.LinkedId);
        }

        return WorldHash.Fnv1A32(values);
    }
}

public struct OverworldSimRules
{
    public int SimulationHz;
    public int InputScale;
    public int WorldBoundaryMilli;

    public int MeleeRangeMilli;
    public int SkillRangeMilli;
    public int PickupRangeMilli;
    public int EnemyContactRangeMilli;

    public int FastAttackBuilderGain;
    public int HeavyAttackSpenderCost;
    public int SkillSpenderCost;

    public int FastAttackBaseCooldownTicks;
    public int HeavyAttackBaseCooldownTicks;
    public int EnemyAttackBaseCooldownTicks;
    public int[] SkillBaseCooldownTicks;

    public static OverworldSimRules Default => new()
    {
        SimulationHz = 60,
        InputScale = 1000,
        WorldBoundaryMilli = 500_000,
        MeleeRangeMilli = 1800,
        SkillRangeMilli = 3200,
        PickupRangeMilli = 1400,
        EnemyContactRangeMilli = 1500,
        FastAttackBuilderGain = 8,
        HeavyAttackSpenderCost = 20,
        SkillSpenderCost = 15,
        FastAttackBaseCooldownTicks = 15,
        HeavyAttackBaseCooldownTicks = 30,
        EnemyAttackBaseCooldownTicks = 45,
        SkillBaseCooldownTicks = new[] { 120, 150, 180, 210, 240, 300, 360, 420 }
    };
}

public static class OverworldSimulator
{
    public static string GetActiveAbilityProfileId(OverworldSimState state) => state.AbilityProfile.Id;

    public static void Step(OverworldSimState state, OverworldSimRules rules)
    {
        if (rules.SimulationHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rules.SimulationHz));
        }

        state.ClearStepEvents();

        var ids = new List<uint>(state.Entities.Keys);
        ids.Sort();

        for (var i = 0; i < ids.Count; i++)
        {
            var entity = state.Entities[ids[i]];
            if (!entity.IsAlive)
            {
                continue;
            }

            TickCooldowns(entity);
            if (entity.Kind == EntityKind.Player)
            {
                RegenSpender(entity, rules);
                IntegrateMovement(entity, rules);
            }
        }

        for (var i = 0; i < ids.Count; i++)
        {
            var entity = state.Entities[ids[i]];
            if (!entity.IsAlive)
            {
                continue;
            }

            if (entity.Kind == EntityKind.Player)
            {
                ProcessPlayerActions(state, entity, rules);
            }
            else if (entity.Kind == EntityKind.Enemy)
            {
                ProcessEnemyAi(state, entity, rules);
            }
        }

        TickZones(state);
        TickLinks(state);

        state.Tick++;
    }

    private static void TickCooldowns(SimEntityState entity)
    {
        if (entity.FastAttackCooldownTicks > 0) entity.FastAttackCooldownTicks--;
        if (entity.HeavyAttackCooldownTicks > 0) entity.HeavyAttackCooldownTicks--;
        if (entity.EnemyAttackCooldownTicks > 0) entity.EnemyAttackCooldownTicks--;
        if (entity.ForcedTargetTicks > 0) entity.ForcedTargetTicks--;
        if (entity.ForcedTargetTicks == 0) entity.ForcedTargetEntityId = 0;
        if (entity.DebugLastConsumedStatusTicks > 0)
        {
            entity.DebugLastConsumedStatusTicks--;
            if (entity.DebugLastConsumedStatusTicks == 0)
            {
                entity.DebugLastConsumedStatusStacks = 0;
            }
        }
        if (entity.DebugLastCastFeedbackTicks > 0)
        {
            entity.DebugLastCastFeedbackTicks--;
            if (entity.DebugLastCastFeedbackTicks == 0)
            {
                entity.DebugLastCastSlotCode = 0;
                entity.DebugLastCastResultCode = 0;
                entity.DebugLastCastTargetTeamCode = 0;
                entity.DebugLastCastAffectedCount = 0;
                entity.DebugLastCastVfxCode = 0;
            }
        }

        for (var i = 0; i < entity.SkillCooldownTicks.Length; i++)
        {
            if (entity.SkillCooldownTicks[i] > 0)
            {
                entity.SkillCooldownTicks[i]--;
            }
        }

        if (entity.DamageReductionTicks > 0)
        {
            entity.DamageReductionTicks--;
            if (entity.DamageReductionTicks == 0)
            {
                entity.DamageReductionPermille = 0;
            }
        }

        if (entity.Statuses.Count > 0)
        {
            var toRemove = new List<string>();
            foreach (var entry in entity.Statuses)
            {
                entry.Value.RemainingTicks--;
                if (entry.Value.RemainingTicks <= 0)
                {
                    toRemove.Add(entry.Key);
                }
            }

            for (var i = 0; i < toRemove.Count; i++)
            {
                entity.Statuses.Remove(toRemove[i]);
            }
        }

        if (entity.Kind == EntityKind.Enemy && entity.ThreatByPlayerEntityId.Count > 0)
        {
            var expiredThreat = new List<uint>();
            var threatKeys = new List<uint>(entity.ThreatByPlayerEntityId.Keys);
            for (var i = 0; i < threatKeys.Count; i++)
            {
                var key = threatKeys[i];
                var next = entity.ThreatByPlayerEntityId[key] - 1;
                if (next <= 0)
                {
                    expiredThreat.Add(key);
                }
                else
                {
                    entity.ThreatByPlayerEntityId[key] = next;
                }
            }

            for (var i = 0; i < expiredThreat.Count; i++)
            {
                entity.ThreatByPlayerEntityId.Remove(expiredThreat[i]);
            }
        }
    }

    private static void RegenSpender(SimEntityState player, OverworldSimRules rules)
    {
        var regenPermille = player.Character.DerivedStats.ResourceRegenPermillePerSecond;
        var regenPerSecondMilli = 2_000 * regenPermille / 1000;
        var regenTick = regenPerSecondMilli / rules.SimulationHz;
        player.SpenderResource = Math.Clamp(
            player.SpenderResource + regenTick,
            0,
            player.Character.DerivedStats.MaxSpenderResource * 1000);
    }

    private static void IntegrateMovement(SimEntityState entity, OverworldSimRules rules)
    {
        var speed = entity.Character.DerivedStats.MoveSpeedMilliPerSecond;
        var deltaX = entity.InputX * speed / (rules.InputScale * rules.SimulationHz);
        var deltaY = entity.InputY * speed / (rules.InputScale * rules.SimulationHz);

        entity.PositionXMilli = Math.Clamp(entity.PositionXMilli + deltaX, -rules.WorldBoundaryMilli, rules.WorldBoundaryMilli);
        entity.PositionYMilli = Math.Clamp(entity.PositionYMilli + deltaY, -rules.WorldBoundaryMilli, rules.WorldBoundaryMilli);
    }

    private static void ProcessPlayerActions(OverworldSimState state, SimEntityState player, OverworldSimRules rules)
    {
        var flags = player.ActionFlags;

        if (flags.HasFlag(InputActionFlags.Pickup))
        {
            TryPickupLoot(state, player, rules);
        }

        // Currency drops auto-loot when in range; click/explicit pickup remains for non-currency loot expansion.
        TryAutoLootCurrency(state, player, rules);

        AbilityRunner.ExecutePlayerCombatActions(state, player, rules);
    }

    private static void ProcessEnemyAi(OverworldSimState state, SimEntityState enemy, OverworldSimRules rules)
    {
        SimEntityState? target = null;
        if (enemy.ForcedTargetTicks > 0 &&
            enemy.ForcedTargetEntityId != 0 &&
            state.TryGetEntity(enemy.ForcedTargetEntityId, out var forcedTarget) &&
            forcedTarget.IsAlive &&
            forcedTarget.Kind == EntityKind.Player)
        {
            target = forcedTarget;
        }

        target ??= FindHighestThreatLivingPlayer(state, enemy);
        target ??= FindNearestLivingPlayer(state, enemy.PositionXMilli, enemy.PositionYMilli);
        if (target is null)
        {
            return;
        }

        var dx = target.PositionXMilli - enemy.PositionXMilli;
        var dy = target.PositionYMilli - enemy.PositionYMilli;
        var distSq = (long)dx * dx + (long)dy * dy;

        if (distSq > (long)rules.EnemyContactRangeMilli * rules.EnemyContactRangeMilli)
        {
            var enemySpeed = 3200;
            if (enemy.Statuses.ContainsKey("status.generic.slow"))
            {
                enemySpeed = enemySpeed * 65 / 100;
            }

            var invLen = 1.0 / Math.Sqrt(distSq);
            enemy.PositionXMilli += (int)(dx * invLen * enemySpeed / rules.SimulationHz);
            enemy.PositionYMilli += (int)(dy * invLen * enemySpeed / rules.SimulationHz);
            return;
        }

        if (enemy.EnemyAttackCooldownTicks == 0)
        {
            ApplyDamage(state, target, 12, enemy, blockedByGuard: target.ActionFlags.HasFlag(InputActionFlags.BlockHold));
            enemy.EnemyAttackCooldownTicks = rules.EnemyAttackBaseCooldownTicks;
        }
    }

    internal static void ApplyDamage(OverworldSimState state, SimEntityState target, int damage, SimEntityState? attacker, bool blockedByGuard)
    {
        var guardReductionPermille = blockedByGuard ? 600 : 0;
        var totalReduction = Math.Min(900, Math.Max(0, target.DamageReductionPermille + guardReductionPermille));
        var reduced = damage * (1000 - totalReduction) / 1000;
        var applied = Math.Max(1, reduced);

        if (target.ShieldMilli > 0)
        {
            var absorbed = Math.Min(target.ShieldMilli, applied * 1000);
            target.ShieldMilli -= absorbed;
            applied -= absorbed / 1000;
        }

        if (applied > 0)
        {
            target.Health = Math.Max(0, target.Health - applied);
        }

        if (attacker is not null && attacker.Kind == EntityKind.Player)
        {
            if (target.Kind == EntityKind.Enemy && applied > 0)
            {
                ApplyThreat(target, attacker.EntityId, applied * 90);
            }

            var knockback = attacker.Character.DerivedStats.KnockbackPermille;
            var dx = target.PositionXMilli - attacker.PositionXMilli;
            var dy = target.PositionYMilli - attacker.PositionYMilli;
            var distSq = (long)dx * dx + (long)dy * dy;
            if (distSq > 0)
            {
                var distance = Math.Sqrt(distSq);
                var impulse = 350 * knockback / 1000;
                target.PositionXMilli += (int)(dx / distance * impulse);
                target.PositionYMilli += (int)(dy / distance * impulse);
            }
        }

        if (target.Health == 0 && target.IsAlive)
        {
            target.IsAlive = false;
            state.SpawnLoot(target.PositionXMilli, target.PositionYMilli, 5);
            if (target.Kind == EntityKind.Enemy)
            {
                state.RecordSimEvent(new SimEventRecord
                {
                    Kind = SimEventKind.EnemyKilled,
                    Tick = state.Tick,
                    PlayerEntityId = attacker?.Kind == EntityKind.Player ? attacker.EntityId : 0,
                    SubjectEntityId = target.EntityId,
                    SubjectObjectId = 0,
                    Value = 1
                });
            }
        }
    }

    internal static void ApplyStatus(SimEntityState target, string statusId, int stacks, int durationTicks)
    {
        if (string.IsNullOrWhiteSpace(statusId))
        {
            return;
        }

        if (!target.Statuses.TryGetValue(statusId, out var status))
        {
            status = new SimStatusState
            {
                Id = statusId,
                Stacks = 0,
                RemainingTicks = durationTicks
            };
            target.Statuses[statusId] = status;
        }

        status.Stacks = Math.Clamp(status.Stacks + Math.Max(1, stacks), 0, 3);
        status.RemainingTicks = Math.Max(status.RemainingTicks, durationTicks);
    }

    internal static int ConsumeStatus(SimEntityState target, string statusId, int maxStacks)
    {
        if (!target.Statuses.TryGetValue(statusId, out var status))
        {
            return 0;
        }

        var consume = Math.Clamp(maxStacks, 1, status.Stacks);
        status.Stacks -= consume;
        if (status.Stacks <= 0)
        {
            target.Statuses.Remove(statusId);
        }

        return consume;
    }

    internal static void Cleanse(SimEntityState target)
    {
        if (target.Statuses.Count == 0)
        {
            return;
        }

        target.Statuses.Clear();
    }

    public static void ApplyTaunt(OverworldSimState state, SimEntityState target, uint taunterEntityId, int durationTicks)
    {
        if (durationTicks <= 0 || taunterEntityId == 0 || target.Kind != EntityKind.Enemy)
        {
            return;
        }

        if (!state.TryGetEntity(taunterEntityId, out var taunter) || !taunter.IsAlive || taunter.Kind != EntityKind.Player)
        {
            return;
        }

        target.ForcedTargetEntityId = taunterEntityId;
        target.ForcedTargetTicks = Math.Max(target.ForcedTargetTicks, durationTicks);
    }

    public static void ApplyThreat(SimEntityState target, uint sourcePlayerEntityId, int amount)
    {
        if (target.Kind != EntityKind.Enemy || sourcePlayerEntityId == 0 || amount <= 0)
        {
            return;
        }

        if (!target.ThreatByPlayerEntityId.TryGetValue(sourcePlayerEntityId, out var current))
        {
            current = 0;
        }

        target.ThreatByPlayerEntityId[sourcePlayerEntityId] = Math.Min(200_000, current + amount);
    }

    public static bool TrySpawnZone(OverworldSimState state, SimEntityState owner, string zoneDefId, int xMilli, int yMilli)
    {
        if (string.IsNullOrWhiteSpace(zoneDefId) || owner.Kind != EntityKind.Player)
        {
            return false;
        }

        if (!state.TryResolveZoneDefinition(zoneDefId, out var definition))
        {
            return false;
        }

        state.SpawnZone(
            zoneDefId,
            owner.EntityId,
            xMilli,
            yMilli,
            definition.DurationTicks,
            definition.TickIntervalTicks,
            definition.RadiusMilli,
            definition.DamagePerPulse,
            definition.HealPerPulse,
            definition.StatusId,
            definition.StatusDurationTicks);
        return true;
    }

    public static bool TryCreateLink(OverworldSimState state, SimEntityState owner, SimEntityState target, string linkDefId)
    {
        if (owner.Kind != EntityKind.Player || target.Kind != EntityKind.Enemy || string.IsNullOrWhiteSpace(linkDefId))
        {
            return false;
        }

        if (!state.TryResolveLinkDefinition(linkDefId, out var definition))
        {
            return false;
        }

        foreach (var existing in state.Links.Values)
        {
            if (existing.OwnerEntityId == owner.EntityId &&
                existing.TargetEntityId == target.EntityId &&
                string.Equals(existing.LinkDefId, linkDefId, StringComparison.Ordinal))
            {
                existing.RemainingTicks = Math.Max(existing.RemainingTicks, definition.DurationTicks);
                return true;
            }
        }

        var ownedLinks = new List<SimLinkState>(4);
        foreach (var link in state.Links.Values)
        {
            if (link.OwnerEntityId == owner.EntityId)
            {
                ownedLinks.Add(link);
            }
        }

        if (ownedLinks.Count >= definition.MaxActiveLinks)
        {
            ownedLinks.Sort((a, b) => a.LinkId.CompareTo(b.LinkId));
            state.RemoveLink(ownedLinks[0].LinkId);
        }

        state.SpawnLink(
            linkDefId,
            owner.EntityId,
            target.EntityId,
            definition.DurationTicks,
            definition.MaxDistanceMilli,
            definition.PullMilliPerTick,
            definition.DamagePerTick);
        return true;
    }

    public static int BreakLinksOwnedBy(OverworldSimState state, uint ownerEntityId)
    {
        if (ownerEntityId == 0 || state.Links.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        var linkIds = new List<uint>(state.Links.Keys);
        for (var i = 0; i < linkIds.Count; i++)
        {
            if (state.Links.TryGetValue(linkIds[i], out var link) && link.OwnerEntityId == ownerEntityId)
            {
                if (state.RemoveLink(link.LinkId))
                {
                    removed++;
                }
            }
        }

        return removed;
    }

    private static void TickZones(OverworldSimState state)
    {
        if (state.Zones.Count == 0)
        {
            return;
        }

        var zoneIds = new List<uint>(state.Zones.Keys);
        zoneIds.Sort();

        var expired = new List<uint>();
        for (var i = 0; i < zoneIds.Count; i++)
        {
            if (!state.Zones.TryGetValue(zoneIds[i], out var zone))
            {
                continue;
            }

            zone.RemainingTicks--;
            zone.TickCountdownTicks--;

            if (zone.TickCountdownTicks <= 0)
            {
                PulseZone(state, zone);
                zone.TickCountdownTicks = zone.TickIntervalTicks;
            }

            if (zone.RemainingTicks <= 0)
            {
                expired.Add(zone.ZoneId);
            }
        }

        for (var i = 0; i < expired.Count; i++)
        {
            state.RemoveZone(expired[i]);
        }
    }

    private static void TickLinks(OverworldSimState state)
    {
        if (state.Links.Count == 0)
        {
            return;
        }

        var linkIds = new List<uint>(state.Links.Keys);
        linkIds.Sort();
        var expired = new List<uint>();
        for (var i = 0; i < linkIds.Count; i++)
        {
            if (!state.Links.TryGetValue(linkIds[i], out var link))
            {
                continue;
            }

            link.RemainingTicks--;
            if (link.RemainingTicks <= 0)
            {
                expired.Add(link.LinkId);
                continue;
            }

            if (!state.TryGetEntity(link.OwnerEntityId, out var owner) || !owner.IsAlive || owner.Kind != EntityKind.Player ||
                !state.TryGetEntity(link.TargetEntityId, out var target) || !target.IsAlive || target.Kind != EntityKind.Enemy)
            {
                expired.Add(link.LinkId);
                continue;
            }

            var dx = owner.PositionXMilli - target.PositionXMilli;
            var dy = owner.PositionYMilli - target.PositionYMilli;
            var distSq = (long)dx * dx + (long)dy * dy;
            var maxDistSq = (long)link.MaxDistanceMilli * link.MaxDistanceMilli;
            if (distSq > maxDistSq)
            {
                expired.Add(link.LinkId);
                continue;
            }

            if (distSq > 0 && link.PullMilliPerTick > 0)
            {
                var distance = Math.Sqrt(distSq);
                var pull = Math.Min(link.PullMilliPerTick, (int)distance);
                target.PositionXMilli += (int)(dx / distance * pull);
                target.PositionYMilli += (int)(dy / distance * pull);
            }

            if (link.DamagePerTick > 0)
            {
                ApplyDamage(state, target, link.DamagePerTick, attacker: null, blockedByGuard: false);
                ApplyThreat(target, owner.EntityId, link.DamagePerTick * 60);
            }
        }

        for (var i = 0; i < expired.Count; i++)
        {
            state.RemoveLink(expired[i]);
        }
    }

    private static void PulseZone(OverworldSimState state, SimZoneState zone)
    {
        var radiusSq = (long)zone.RadiusMilli * zone.RadiusMilli;
        state.TryGetEntity(zone.OwnerEntityId, out var owner);
        foreach (var entity in state.Entities.Values)
        {
            if (!entity.IsAlive)
            {
                continue;
            }

            var dx = entity.PositionXMilli - zone.PositionXMilli;
            var dy = entity.PositionYMilli - zone.PositionYMilli;
            var distSq = (long)dx * dx + (long)dy * dy;
            if (distSq > radiusSq)
            {
                continue;
            }

            if (entity.Kind == EntityKind.Enemy && zone.DamagePerPulse > 0)
            {
                ApplyDamage(state, entity, zone.DamagePerPulse, owner, blockedByGuard: false);
            }

            if (entity.Kind == EntityKind.Player && zone.HealPerPulse > 0)
            {
                entity.Health = Math.Clamp(entity.Health + zone.HealPerPulse, 0, entity.Character.DerivedStats.MaxHealth);
            }

            if (entity.Kind == EntityKind.Enemy && !string.IsNullOrWhiteSpace(zone.StatusId))
            {
                ApplyStatus(entity, zone.StatusId!, 1, zone.StatusDurationTicks);
            }

            if (entity.Kind == EntityKind.Player && zone.ZoneDefId == "zone.exorcist.warden.abjuration_field")
            {
                if (entity.DamageReductionPermille < 200)
                {
                    entity.DamageReductionPermille = 200;
                }

                entity.DamageReductionTicks = Math.Max(entity.DamageReductionTicks, 22);
                entity.ShieldMilli = Math.Min(48_000, entity.ShieldMilli + 2_000);
            }
        }
    }


    private static void TryPickupLoot(OverworldSimState state, SimEntityState player, OverworldSimRules rules)
    {
        var lootIds = new List<uint>(state.LootDrops.Keys);
        lootIds.Sort();

        foreach (var lootId in lootIds)
        {
            var loot = state.LootDrops[lootId];
            if (loot.Claimed)
            {
                continue;
            }

            var dx = loot.PositionXMilli - player.PositionXMilli;
            var dy = loot.PositionYMilli - player.PositionYMilli;
            var distSq = (long)dx * dx + (long)dy * dy;
            if (distSq > (long)rules.PickupRangeMilli * rules.PickupRangeMilli)
            {
                continue;
            }

            loot.Claimed = true;
            player.Character.Currency += loot.CurrencyAmount;
            state.RecordLootGrant(player.EntityId, loot.LootId, loot.CurrencyAmount, autoLoot: false);
            state.RecordSimEvent(new SimEventRecord
            {
                Kind = SimEventKind.TokenCollected,
                Tick = state.Tick,
                PlayerEntityId = player.EntityId,
                SubjectEntityId = 0,
                SubjectObjectId = loot.LootId,
                Value = loot.CurrencyAmount
            });
            break;
        }
    }

    private static void TryAutoLootCurrency(OverworldSimState state, SimEntityState player, OverworldSimRules rules)
    {
        var lootIds = new List<uint>(state.LootDrops.Keys);
        lootIds.Sort();

        foreach (var lootId in lootIds)
        {
            var loot = state.LootDrops[lootId];
            if (loot.Claimed || loot.CurrencyAmount <= 0 || !loot.AutoLoot)
            {
                continue;
            }

            var dx = loot.PositionXMilli - player.PositionXMilli;
            var dy = loot.PositionYMilli - player.PositionYMilli;
            var distSq = (long)dx * dx + (long)dy * dy;
            if (distSq > (long)rules.PickupRangeMilli * rules.PickupRangeMilli)
            {
                continue;
            }

            loot.Claimed = true;
            player.Character.Currency += loot.CurrencyAmount;
            state.RecordLootGrant(player.EntityId, loot.LootId, loot.CurrencyAmount, autoLoot: true);
            state.RecordSimEvent(new SimEventRecord
            {
                Kind = SimEventKind.TokenCollected,
                Tick = state.Tick,
                PlayerEntityId = player.EntityId,
                SubjectEntityId = 0,
                SubjectObjectId = loot.LootId,
                Value = loot.CurrencyAmount
            });
        }
    }

    internal static SimEntityState? FindNearestEnemyInRange(OverworldSimState state, int x, int y, int rangeMilli)
    {
        SimEntityState? nearest = null;
        long nearestDistSq = long.MaxValue;
        var maxDistSq = (long)rangeMilli * rangeMilli;

        foreach (var entity in state.Entities.Values)
        {
            if (entity.Kind != EntityKind.Enemy || !entity.IsAlive)
            {
                continue;
            }

            var dx = entity.PositionXMilli - x;
            var dy = entity.PositionYMilli - y;
            var distSq = (long)dx * dx + (long)dy * dy;
            if (distSq <= maxDistSq && distSq < nearestDistSq)
            {
                nearest = entity;
                nearestDistSq = distSq;
            }
        }

        return nearest;
    }

    internal static void FindEnemiesInRange(
        OverworldSimState state,
        int x,
        int y,
        int rangeMilli,
        int maxTargets,
        List<SimEntityState> results)
    {
        results.Clear();
        if (maxTargets <= 0)
        {
            return;
        }

        var maxDistSq = (long)rangeMilli * rangeMilli;
        var candidates = new List<(SimEntityState Entity, long DistSq)>();
        foreach (var entity in state.Entities.Values)
        {
            if (entity.Kind != EntityKind.Enemy || !entity.IsAlive)
            {
                continue;
            }

            var dx = entity.PositionXMilli - x;
            var dy = entity.PositionYMilli - y;
            var distSq = (long)dx * dx + (long)dy * dy;
            if (distSq <= maxDistSq)
            {
                candidates.Add((entity, distSq));
            }
        }

        candidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
        var count = Math.Min(maxTargets, candidates.Count);
        for (var i = 0; i < count; i++)
        {
            results.Add(candidates[i].Entity);
        }
    }

    internal static void FindPlayersInRange(
        OverworldSimState state,
        int x,
        int y,
        int rangeMilli,
        int maxTargets,
        List<SimEntityState> results)
    {
        results.Clear();
        if (maxTargets <= 0)
        {
            return;
        }

        var maxDistSq = (long)rangeMilli * rangeMilli;
        var candidates = new List<(SimEntityState Entity, long DistSq)>();
        foreach (var entity in state.Entities.Values)
        {
            if (entity.Kind != EntityKind.Player || !entity.IsAlive)
            {
                continue;
            }

            var dx = entity.PositionXMilli - x;
            var dy = entity.PositionYMilli - y;
            var distSq = (long)dx * dx + (long)dy * dy;
            if (distSq <= maxDistSq)
            {
                candidates.Add((entity, distSq));
            }
        }

        candidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
        var count = Math.Min(maxTargets, candidates.Count);
        for (var i = 0; i < count; i++)
        {
            results.Add(candidates[i].Entity);
        }
    }

    internal static void FindAnyInRange(
        OverworldSimState state,
        int x,
        int y,
        int rangeMilli,
        int maxTargets,
        List<SimEntityState> results)
    {
        results.Clear();
        if (maxTargets <= 0)
        {
            return;
        }

        var maxDistSq = (long)rangeMilli * rangeMilli;
        var candidates = new List<(SimEntityState Entity, long DistSq)>();
        foreach (var entity in state.Entities.Values)
        {
            if (!entity.IsAlive || (entity.Kind != EntityKind.Player && entity.Kind != EntityKind.Enemy))
            {
                continue;
            }

            var dx = entity.PositionXMilli - x;
            var dy = entity.PositionYMilli - y;
            var distSq = (long)dx * dx + (long)dy * dy;
            if (distSq <= maxDistSq)
            {
                candidates.Add((entity, distSq));
            }
        }

        candidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
        var count = Math.Min(maxTargets, candidates.Count);
        for (var i = 0; i < count; i++)
        {
            results.Add(candidates[i].Entity);
        }
    }

    private static SimEntityState? FindNearestLivingPlayer(OverworldSimState state, int x, int y)
    {
        SimEntityState? nearest = null;
        long nearestDistSq = long.MaxValue;

        foreach (var entity in state.Entities.Values)
        {
            if (entity.Kind != EntityKind.Player || !entity.IsAlive)
            {
                continue;
            }

            var dx = entity.PositionXMilli - x;
            var dy = entity.PositionYMilli - y;
            var distSq = (long)dx * dx + (long)dy * dy;
            if (distSq < nearestDistSq)
            {
                nearest = entity;
                nearestDistSq = distSq;
            }
        }

        return nearest;
    }

    private static SimEntityState? FindHighestThreatLivingPlayer(OverworldSimState state, SimEntityState enemy)
    {
        if (enemy.ThreatByPlayerEntityId.Count == 0)
        {
            return null;
        }

        SimEntityState? best = null;
        var bestThreat = int.MinValue;
        foreach (var kvp in enemy.ThreatByPlayerEntityId)
        {
            if (!state.TryGetEntity(kvp.Key, out var candidate) || !candidate.IsAlive || candidate.Kind != EntityKind.Player)
            {
                continue;
            }

            if (kvp.Value > bestThreat)
            {
                bestThreat = kvp.Value;
                best = candidate;
            }
        }

        return best;
    }

    internal static int ScaleByAttackSpeed(int baseTicks, int attackSpeedPermille, int minimumTicks)
    {
        var scaled = baseTicks * 1000 / Math.Max(500, attackSpeedPermille);
        return Math.Max(minimumTicks, scaled);
    }

}
