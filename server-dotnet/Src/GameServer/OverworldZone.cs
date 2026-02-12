using System;
using System.Collections.Generic;
using Armament.SharedSim.Protocol;
using Armament.SharedSim.Sim;

namespace Armament.GameServer;

public sealed class OverworldZone
{
    public const uint PortalEntityId = 900_001;
    private const uint ZoneEntityBaseId = 3_000_000;
    private const uint LinkEntityBaseId = 4_000_000;

    private readonly Dictionary<uint, uint> _entityByClient = new();
    private readonly OverworldSimState _simState = new();
    private readonly OverworldSimRules _simRules;
    private readonly CharacterStatTuning _statTuning;
    private readonly List<SimLootGrantEvent> _lastLootGrantEvents = new();
    private readonly string _fallbackAbilityProfileId;

    private uint _nextEntityId = 1;
    private bool _enemySpawned;
    private bool _testLootSpawned;

    public OverworldZone(int simulationHz, IReadOnlyDictionary<string, SimAbilityProfile>? abilityProfiles = null, string? fallbackAbilityProfileId = null)
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

        _fallbackAbilityProfileId = ResolveAbilityProfileId(fallbackAbilityProfileId);
        _simState.DefaultAbilityProfileId = _fallbackAbilityProfileId;
    }

    public int PlayerCount => _entityByClient.Count;
    public ZoneKind ZoneKind => ZoneKind.Overworld;
    public uint InstanceId => 0;
    public IReadOnlyList<SimLootGrantEvent> LastLootGrantEvents => _lastLootGrantEvents;

    public uint Join(uint clientId, string name, string? abilityProfileId = null)
    {
        if (_entityByClient.TryGetValue(clientId, out var existingEntityId))
        {
            return existingEntityId;
        }

        if (!_enemySpawned)
        {
            SpawnEnemy();
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
        _lastLootGrantEvents.Clear();
        for (var i = 0; i < _simState.LootGrantEvents.Count; i++)
        {
            _lastLootGrantEvents.Add(_simState.LootGrantEvents[i]);
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

    public WorldSnapshot BuildSnapshot(uint serverTick)
    {
        var snapshot = new WorldSnapshot { ServerTick = serverTick, ZoneKind = ZoneKind.Overworld, InstanceId = 0 };

        AppendEntitySnapshots(snapshot);

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
                FastCooldownTicks = ClampByte(entity.FastAttackCooldownTicks),
                HeavyCooldownTicks = ClampByte(entity.HeavyAttackCooldownTicks),
                Skill1CooldownTicks = ClampByte(entity.SkillCooldownTicks[0]),
                Skill2CooldownTicks = ClampByte(entity.SkillCooldownTicks[1]),
                Skill3CooldownTicks = ClampByte(entity.SkillCooldownTicks[2]),
                Skill4CooldownTicks = ClampByte(entity.SkillCooldownTicks[3]),
                Skill5CooldownTicks = ClampByte(entity.SkillCooldownTicks[4]),
                Skill6CooldownTicks = ClampByte(entity.SkillCooldownTicks[5]),
                Skill7CooldownTicks = ClampByte(entity.SkillCooldownTicks[6]),
                Skill8CooldownTicks = ClampByte(entity.SkillCooldownTicks[7]),
                AggroTargetEntityId = ResolveAggroTarget(entity),
                AggroThreatValue = ResolveAggroThreat(entity),
                ForcedTargetTicks = ClampByte(entity.ForcedTargetTicks),
                DebugPrimaryStatusStacks = ResolvePrimaryStatusStacks(entity),
                DebugConsumedStatusStacks = ClampByte(entity.DebugLastConsumedStatusStacks),
                DebugLastCastSlotCode = ClampByte(entity.DebugLastCastSlotCode),
                DebugLastCastResultCode = ClampByte(entity.DebugLastCastResultCode),
                DebugLastCastTargetTeamCode = ClampByte(entity.DebugLastCastTargetTeamCode),
                DebugLastCastAffectedCount = ClampByte(entity.DebugLastCastAffectedCount),
                DebugLastCastVfxCode = (ushort)ClampUShort(entity.DebugLastCastVfxCode)
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

        foreach (var zone in _simState.Zones.Values)
        {
            snapshot.Entities.Add(new EntitySnapshot
            {
                EntityId = ZoneEntityBaseId + zone.ZoneId,
                Kind = EntityKind.Zone,
                QuantizedX = Quantization.QuantizePosition(zone.PositionXMilli / 1000f),
                QuantizedY = Quantization.QuantizePosition(zone.PositionYMilli / 1000f),
                Health = (ushort)ClampUShort(zone.RemainingTicks),
                BuilderResource = (ushort)ClampUShort(zone.RadiusMilli / 100),
                SpenderResource = ResolveZoneTypeCode(zone.ZoneDefId),
                Currency = 0
            });
        }

        foreach (var link in _simState.Links.Values)
        {
            if (!_simState.TryGetEntity(link.OwnerEntityId, out var owner) || !_simState.TryGetEntity(link.TargetEntityId, out var target))
            {
                continue;
            }

            var midX = (owner.PositionXMilli + target.PositionXMilli) / 2;
            var midY = (owner.PositionYMilli + target.PositionYMilli) / 2;
            snapshot.Entities.Add(new EntitySnapshot
            {
                EntityId = LinkEntityBaseId + link.LinkId,
                Kind = EntityKind.Link,
                QuantizedX = Quantization.QuantizePosition(midX / 1000f),
                QuantizedY = Quantization.QuantizePosition(midY / 1000f),
                Health = (ushort)ClampUShort(link.RemainingTicks),
                BuilderResource = (ushort)ClampUShort((int)link.OwnerEntityId),
                SpenderResource = (ushort)ClampUShort((int)link.TargetEntityId),
                Currency = 0
            });
        }
    }

    private void SpawnEnemy()
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

    private static ushort ResolveZoneTypeCode(string zoneDefId)
    {
        return zoneDefId switch
        {
            "zone.exorcist.warden.abjuration_field" => 1,
            "zone.exorcist.inquisitor.abjuration_field" => 1,
            "zone.bastion.bulwark.fissure" => 2,
            "zone.bastion.cataclysm.fissure" => 2,
            "zone.bastion.bulwark.caldera" => 3,
            "zone.bastion.cataclysm.caldera" => 3,
            "zone.tidebinder.tidecaller.soothing_pool" => 4,
            "zone.tidebinder.tidecaller.maelstrom" => 5,
            "zone.tidebinder.tempest.vortex_pool" => 6,
            "zone.tidebinder.tempest.maelstrom" => 7,
            "zone.arbiter.aegis.seal" => 8,
            "zone.arbiter.aegis.ward" => 8,
            "zone.arbiter.edict.seal" => 8,
            "zone.arbiter.edict.lattice" => 8,
            "zone.arbiter.aegis.decree" => 9,
            "zone.arbiter.edict.decree" => 9,
            _ => 0
        };
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
