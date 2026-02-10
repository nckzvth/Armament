using System;
using System.Collections.Generic;
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

    private uint _nextEntityId = 1;
    private bool _enemySpawned;
    private bool _testLootSpawned;

    public OverworldZone(int simulationHz)
    {
        _simRules = OverworldSimRules.Default;
        _simRules.SimulationHz = simulationHz;
        _statTuning = CharacterStatTuning.Default;
    }

    public int PlayerCount => _entityByClient.Count;
    public ZoneKind ZoneKind => ZoneKind.Overworld;
    public uint InstanceId => 0;
    public IReadOnlyList<SimLootGrantEvent> LastLootGrantEvents => _lastLootGrantEvents;

    public uint Join(uint clientId, string name)
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
            Health = 100
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
            Health = 120
        };

        enemy.Character.Attributes = new CharacterAttributes
        {
            Might = 9,
            Will = 6,
            Alacrity = 8,
            Constitution = 8
        };
        enemy.Character.RecalculateDerivedStats(_statTuning);
        enemy.Health = 120;

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
            SpenderResource = entity.SpenderResource
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
}
