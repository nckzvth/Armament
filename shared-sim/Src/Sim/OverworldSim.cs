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
    public int BuilderResource { get; set; }
    public int SpenderResource { get; set; }

    public int FastAttackCooldownTicks { get; set; }
    public int HeavyAttackCooldownTicks { get; set; }
    public int EnemyAttackCooldownTicks { get; set; }
    public int[] SkillCooldownTicks { get; } = new int[8];
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

public struct SimLootGrantEvent
{
    public uint PlayerEntityId { get; set; }
    public uint LootId { get; set; }
    public int CurrencyAmount { get; set; }
    public bool AutoLoot { get; set; }
}

public sealed class OverworldSimState
{
    private readonly Dictionary<uint, SimEntityState> _entities = new();
    private readonly Dictionary<uint, SimLootDrop> _lootDrops = new();
    private readonly List<SimLootGrantEvent> _lootGrantEvents = new();
    private uint _nextLootId = 1_000_000;

    public uint Tick { get; set; }
    public uint Seed { get; set; } = 1;
    public IReadOnlyDictionary<uint, SimEntityState> Entities => _entities;
    public IReadOnlyDictionary<uint, SimLootDrop> LootDrops => _lootDrops;
    public IReadOnlyList<SimLootGrantEvent> LootGrantEvents => _lootGrantEvents;

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

    public void ClearStepEvents()
    {
        _lootGrantEvents.Clear();
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

    public uint ComputeWorldHash()
    {
        var values = new List<uint>(_entities.Count * 12 + _lootDrops.Count * 5 + 4)
        {
            Tick,
            (uint)_entities.Count,
            (uint)_lootDrops.Count,
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
            values.Add(unchecked((uint)entity.BuilderResource));
            values.Add(unchecked((uint)entity.SpenderResource));
            values.Add(unchecked((uint)entity.Character.Currency));
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

        state.Tick++;
    }

    private static void TickCooldowns(SimEntityState entity)
    {
        if (entity.FastAttackCooldownTicks > 0) entity.FastAttackCooldownTicks--;
        if (entity.HeavyAttackCooldownTicks > 0) entity.HeavyAttackCooldownTicks--;
        if (entity.EnemyAttackCooldownTicks > 0) entity.EnemyAttackCooldownTicks--;

        for (var i = 0; i < entity.SkillCooldownTicks.Length; i++)
        {
            if (entity.SkillCooldownTicks[i] > 0)
            {
                entity.SkillCooldownTicks[i]--;
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

        if (flags.HasFlag(InputActionFlags.FastAttackHold) && player.FastAttackCooldownTicks == 0)
        {
            var enemy = FindNearestEnemyInRange(state, player.PositionXMilli, player.PositionYMilli, rules.MeleeRangeMilli);
            if (enemy is not null)
            {
                var damage = player.Character.DerivedStats.BaseMeleeDamage;
                ApplyDamage(state, enemy, damage, player);
            }

            player.BuilderResource = Math.Clamp(
                player.BuilderResource + rules.FastAttackBuilderGain * 1000,
                0,
                player.Character.DerivedStats.MaxBuilderResource * 1000);

            player.FastAttackCooldownTicks = ScaleByAttackSpeed(rules.FastAttackBaseCooldownTicks, player.Character.DerivedStats.AttackSpeedPermille, 4);
        }

        if (flags.HasFlag(InputActionFlags.HeavyAttackHold) && player.HeavyAttackCooldownTicks == 0)
        {
            var costMilli = rules.HeavyAttackSpenderCost * 1000;
            if (player.SpenderResource >= costMilli)
            {
                var enemy = FindNearestEnemyInRange(state, player.PositionXMilli, player.PositionYMilli, rules.MeleeRangeMilli + 300);
                if (enemy is not null)
                {
                    var damage = player.Character.DerivedStats.BaseMeleeDamage * 18 / 10;
                    ApplyDamage(state, enemy, damage, player);
                }

                player.SpenderResource -= costMilli;
                player.HeavyAttackCooldownTicks = ScaleByAttackSpeed(rules.HeavyAttackBaseCooldownTicks, player.Character.DerivedStats.AttackSpeedPermille, 8);
            }
        }

        ProcessSkillActions(state, player, flags, rules);
    }

    private static void ProcessSkillActions(OverworldSimState state, SimEntityState player, InputActionFlags flags, OverworldSimRules rules)
    {
        for (var i = 0; i < 8; i++)
        {
            var mask = (InputActionFlags)(1u << (i + 3));
            if (!flags.HasFlag(mask) || player.SkillCooldownTicks[i] > 0)
            {
                continue;
            }

            var costMilli = rules.SkillSpenderCost * 1000;
            if (player.SpenderResource < costMilli)
            {
                continue;
            }

            var enemy = FindNearestEnemyInRange(state, player.PositionXMilli, player.PositionYMilli, rules.SkillRangeMilli);
            if (enemy is not null)
            {
                var potency = player.Character.DerivedStats.SkillPotencyPermille;
                var damage = (player.Character.DerivedStats.BaseMeleeDamage + 6 + i * 2) * potency / 1000;
                ApplyDamage(state, enemy, damage, player);
            }

            player.SpenderResource -= costMilli;
            var baseCooldown = i < rules.SkillBaseCooldownTicks.Length ? rules.SkillBaseCooldownTicks[i] : 240;
            player.SkillCooldownTicks[i] = ScaleByAttackSpeed(baseCooldown, player.Character.DerivedStats.AttackSpeedPermille, 20);
        }
    }

    private static void ProcessEnemyAi(OverworldSimState state, SimEntityState enemy, OverworldSimRules rules)
    {
        var target = FindNearestLivingPlayer(state, enemy.PositionXMilli, enemy.PositionYMilli);
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
            var invLen = 1.0 / Math.Sqrt(distSq);
            enemy.PositionXMilli += (int)(dx * invLen * enemySpeed / rules.SimulationHz);
            enemy.PositionYMilli += (int)(dy * invLen * enemySpeed / rules.SimulationHz);
            return;
        }

        if (enemy.EnemyAttackCooldownTicks == 0)
        {
            var blocked = target.ActionFlags.HasFlag(InputActionFlags.BlockHold);
            var rawDamage = 12;
            var applied = blocked ? rawDamage * 40 / 100 : rawDamage;
            target.Health = Math.Max(0, target.Health - applied);
            if (target.Health == 0)
            {
                target.IsAlive = false;
            }

            enemy.EnemyAttackCooldownTicks = rules.EnemyAttackBaseCooldownTicks;
        }
    }

    private static void ApplyDamage(OverworldSimState state, SimEntityState target, int damage, SimEntityState? attacker)
    {
        target.Health = Math.Max(0, target.Health - damage);

        if (attacker is not null)
        {
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
        }
    }

    private static SimEntityState? FindNearestEnemyInRange(OverworldSimState state, int x, int y, int rangeMilli)
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

    private static int ScaleByAttackSpeed(int baseTicks, int attackSpeedPermille, int minimumTicks)
    {
        var scaled = baseTicks * 1000 / Math.Max(500, attackSpeedPermille);
        return Math.Max(minimumTicks, scaled);
    }
}
