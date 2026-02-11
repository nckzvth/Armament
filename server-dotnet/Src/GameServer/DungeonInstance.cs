using System.Collections.Generic;
using Armament.SharedSim.Protocol;
using Armament.SharedSim.Sim;

namespace Armament.GameServer;

public sealed class DungeonInstance
{
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
            if (!entity.IsAlive)
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
                Currency = (ushort)ClampUShort(entity.Character.Currency)
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
            Health = 260
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
            Health = 140
        };
        add1.Character.Attributes = new CharacterAttributes { Might = 10, Will = 7, Alacrity = 9, Constitution = 10 };
        add1.Character.RecalculateDerivedStats(_statTuning);
        _simState.UpsertEntity(add1);
    }

    private static int ClampUShort(int value)
    {
        return value < 0 ? 0 : (value > ushort.MaxValue ? ushort.MaxValue : value);
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
