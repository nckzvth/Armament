using System.Collections.Generic;
using Armament.SharedSim.Protocol;
using Armament.SharedSim.Sim;

namespace Armament.GameServer;

public sealed class DungeonInstance
{
    private const uint ZoneEntityBaseId = 3_000_000;
    private const uint LinkEntityBaseId = 4_000_000;
    private readonly uint _instanceId;
    private readonly Dictionary<uint, uint> _entityByClient = new();
    private readonly OverworldSimState _simState = new();
    private readonly OverworldSimRules _simRules;
    private readonly CharacterStatTuning _statTuning;
    private readonly List<SimLootGrantEvent> _lastLootGrantEvents = new();
    private readonly string _fallbackAbilityProfileId;
    private uint _nextEntityId = 50_000;
    private bool _bossSpawned;

    public DungeonInstance(uint instanceId, int simulationHz, IReadOnlyDictionary<string, SimAbilityProfile>? abilityProfiles = null, string? fallbackAbilityProfileId = null)
    {
        _instanceId = instanceId;
        _simRules = OverworldSimRules.Default;
        _simRules.SimulationHz = simulationHz;
        _simRules.SkillRangeMilli = 3600;
        _simRules.PickupRangeMilli = 1700;
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

    public uint InstanceId => _instanceId;
    public int PlayerCount => _entityByClient.Count;
    public IReadOnlyList<SimLootGrantEvent> LastLootGrantEvents => _lastLootGrantEvents;

    public uint JoinTransferred(uint clientId, in PlayerTransferState transferState)
    {
        if (!_bossSpawned)
        {
            SpawnBossPack();
            _bossSpawned = true;
        }

        var entityId = _nextEntityId++;
        var entity = new SimEntityState
        {
            EntityId = entityId,
            Kind = EntityKind.Player,
            PositionXMilli = 0,
            PositionYMilli = -1_000
        };

        entity.Character.CharacterId = transferState.CharacterId;
        entity.Character.Attributes = transferState.Attributes;
        entity.Character.Level = transferState.Level;
        entity.Character.Experience = transferState.Experience;
        entity.Character.Currency = transferState.Currency;
        entity.Character.RecalculateDerivedStats(_statTuning);

        entity.Health = transferState.Health > 0 ? transferState.Health : entity.Character.DerivedStats.MaxHealth;
        entity.BuilderResource = transferState.BuilderResource;
        entity.SpenderResource = transferState.SpenderResource;
        entity.AbilityProfileId = ResolveAbilityProfileId(transferState.AbilityProfileId);

        _simState.UpsertEntity(entity);
        _entityByClient[clientId] = entityId;
        return entityId;
    }

    public bool TryTransferOut(uint clientId, out PlayerTransferState transferState)
    {
        transferState = default;
        if (!_entityByClient.TryGetValue(clientId, out var entityId) || !_simState.TryGetEntity(entityId, out var entity))
        {
            return false;
        }

        transferState = new PlayerTransferState
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

    public void ApplyInput(uint clientId, short x, short y, InputActionFlags actionFlags)
    {
        if (!_entityByClient.TryGetValue(clientId, out var entityId) || !_simState.TryGetEntity(entityId, out var entity))
        {
            return;
        }

        entity.InputX = x;
        entity.InputY = y;
        entity.ActionFlags = actionFlags;
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
        var snapshot = new WorldSnapshot
        {
            ServerTick = serverTick,
            ZoneKind = ZoneKind.Dungeon,
            InstanceId = _instanceId
        };

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
                DebugLastCastFeedbackTicks = ClampByte(entity.DebugLastCastFeedbackTicks)
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

        return snapshot;
    }

    private void SpawnBossPack()
    {
        var miniBoss = new SimEntityState
        {
            EntityId = _nextEntityId++,
            Kind = EntityKind.Enemy,
            PositionXMilli = 2_500,
            PositionYMilli = 0,
            Health = 900
        };
        miniBoss.Character.Attributes = new CharacterAttributes
        {
            Might = 15,
            Will = 10,
            Alacrity = 9,
            Constitution = 14
        };
        miniBoss.Character.RecalculateDerivedStats(_statTuning);
        _simState.UpsertEntity(miniBoss);

        var add1 = new SimEntityState
        {
            EntityId = _nextEntityId++,
            Kind = EntityKind.Enemy,
            PositionXMilli = 3_400,
            PositionYMilli = -700,
            Health = 520
        };
        add1.Character.Attributes = new CharacterAttributes { Might = 10, Will = 7, Alacrity = 9, Constitution = 10 };
        add1.Character.RecalculateDerivedStats(_statTuning);
        _simState.UpsertEntity(add1);
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
