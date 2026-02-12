#nullable enable
using System;
using System.Collections.Generic;
using Armament.SharedSim.Protocol;

namespace Armament.SharedSim.Sim;

public enum SimAbilityInputBehavior : byte
{
    Tap = 1,
    HoldRepeat = 2,
    HoldReleaseCharge = 3
}

public enum SimAbilitySlot : byte
{
    Lmb = 0,
    Rmb = 1,
    Shift = 2,
    E = 3,
    R = 4,
    Q = 5,
    T = 6,
    Skill1 = 7,
    Skill2 = 8,
    Skill3 = 9,
    Skill4 = 10
}

public sealed class SimAbilityDefinition
{
    public string Id { get; set; } = string.Empty;
    public SimAbilitySlot Slot { get; set; }
    public SimAbilityInputBehavior InputBehavior { get; set; } = SimAbilityInputBehavior.Tap;
    public string AnimTag { get; set; } = string.Empty;
    public string VfxTag { get; set; } = string.Empty;
    public int BaseCooldownTicks { get; set; }
    public int CooldownMinTicks { get; set; }
    public int RangeMilli { get; set; }
    public int SpenderCostMilli { get; set; }
    public int BuilderGainMilli { get; set; }
    public bool HasDamageEffect { get; set; } = true;
    public int DamageCoefficientPermille { get; set; } = 1000;
    public int DamageFlat { get; set; }
    public int MaxTargets { get; set; } = 1;
    public SimTargetTeam TargetTeam { get; set; } = SimTargetTeam.Enemy;
    public List<SimAbilityEffect> Effects { get; } = new();
    public int RequiredSpenderCostMilli { get; set; }
}

public enum SimTargetTeam : byte
{
    Enemy = 1,
    Ally = 2,
    Any = 3,
    Self = 4
}

public enum SimAbilityPrimitive : byte
{
    None = 0,
    StartCooldown = 1,
    SpendResource = 2,
    GainResource = 3,
    DealDamage = 4,
    ApplyShield = 5,
    ApplyDamageReduction = 6,
    ApplyStatus = 7,
    ConsumeStatus = 8,
    Cleanse = 9,
    Taunt = 10,
    ApplyCc = 11,
    SpawnZone = 12,
    HitscanTrace = 13,
    FireProjectile = 14,
    AddThreat = 15,
    CreateLink = 16,
    BreakLink = 17,
    Heal = 18
}

public sealed class SimAbilityEffect
{
    public SimAbilityPrimitive Primitive { get; set; }
    public string? StatusId { get; set; }
    public string? ZoneDefId { get; set; }
    public string? LinkDefId { get; set; }
    public int Amount { get; set; }
    public int CoefficientPermille { get; set; }
    public int Flat { get; set; }
}

public sealed class SimAbilityProfile
{
    public string Id { get; set; } = "builtin.v1";
    public Dictionary<InputActionFlags, SimAbilityDefinition> AbilitiesByFlag { get; } = new();
}

public static class SimAbilityProfiles
{
    public static SimAbilityProfile BuiltinV1 { get; } = BuildBuiltinV1();

    private static SimAbilityProfile BuildBuiltinV1()
    {
        var profile = new SimAbilityProfile { Id = "builtin.v1" };

        Add(profile, new SimAbilityDefinition
        {
            Id = "ability.builtin.fast_attack",
            Slot = SimAbilitySlot.Lmb,
            InputBehavior = SimAbilityInputBehavior.HoldRepeat,
            BaseCooldownTicks = OverworldSimRules.Default.FastAttackBaseCooldownTicks,
            CooldownMinTicks = 4,
            RangeMilli = OverworldSimRules.Default.MeleeRangeMilli,
            SpenderCostMilli = 0,
            BuilderGainMilli = OverworldSimRules.Default.FastAttackBuilderGain * 1000,
            HasDamageEffect = true
            ,
            DamageCoefficientPermille = 1000,
            DamageFlat = 0
        });

        Add(profile, new SimAbilityDefinition
        {
            Id = "ability.builtin.heavy_attack",
            Slot = SimAbilitySlot.Rmb,
            InputBehavior = SimAbilityInputBehavior.HoldRepeat,
            BaseCooldownTicks = OverworldSimRules.Default.HeavyAttackBaseCooldownTicks,
            CooldownMinTicks = 8,
            RangeMilli = OverworldSimRules.Default.MeleeRangeMilli + 300,
            SpenderCostMilli = OverworldSimRules.Default.HeavyAttackSpenderCost * 1000,
            BuilderGainMilli = 0,
            HasDamageEffect = true
            ,
            DamageCoefficientPermille = 1000,
            DamageFlat = 0
        });

        for (var i = 0; i < 8; i++)
        {
            Add(profile, new SimAbilityDefinition
            {
                Id = $"ability.builtin.skill_{i + 1}",
                Slot = (SimAbilitySlot)((int)SimAbilitySlot.E + i),
                InputBehavior = SimAbilityInputBehavior.Tap,
                BaseCooldownTicks = i < OverworldSimRules.Default.SkillBaseCooldownTicks.Length
                    ? OverworldSimRules.Default.SkillBaseCooldownTicks[i]
                    : 240,
                CooldownMinTicks = 20,
                RangeMilli = OverworldSimRules.Default.SkillRangeMilli,
                SpenderCostMilli = OverworldSimRules.Default.SkillSpenderCost * 1000,
                BuilderGainMilli = 0,
                HasDamageEffect = true,
                DamageCoefficientPermille = 1000,
                DamageFlat = 0
            });
        }

        return profile;
    }

    private static void Add(SimAbilityProfile profile, SimAbilityDefinition definition)
    {
        profile.AbilitiesByFlag[MapTriggerFlag(definition.Slot)] = definition;
    }

    internal static InputActionFlags MapTriggerFlag(SimAbilitySlot slot)
    {
        return slot switch
        {
            SimAbilitySlot.Lmb => InputActionFlags.FastAttackHold,
            SimAbilitySlot.Rmb => InputActionFlags.HeavyAttackHold,
            SimAbilitySlot.Shift => InputActionFlags.BlockHold,
            SimAbilitySlot.E => InputActionFlags.Skill1,
            SimAbilitySlot.R => InputActionFlags.Skill2,
            SimAbilitySlot.Q => InputActionFlags.Skill3,
            SimAbilitySlot.T => InputActionFlags.Skill4,
            SimAbilitySlot.Skill1 => InputActionFlags.Skill5,
            SimAbilitySlot.Skill2 => InputActionFlags.Skill6,
            SimAbilitySlot.Skill3 => InputActionFlags.Skill7,
            SimAbilitySlot.Skill4 => InputActionFlags.Skill8,
            _ => InputActionFlags.None
        };
    }
}

internal static class AbilityRunner
{
    private const byte CastResultSuccess = 1;
    private const byte CastResultNoTarget = 2;
    private const byte CastResultCooldown = 3;
    private const byte CastResultInsufficientResource = 4;

    public static void ExecutePlayerCombatActions(OverworldSimState state, SimEntityState player, in OverworldSimRules rules)
    {
        var profile = state.ResolveAbilityProfile(player.AbilityProfileId);
        foreach (var kvp in profile.AbilitiesByFlag)
        {
            if (!player.ActionFlags.HasFlag(kvp.Key))
            {
                continue;
            }

            TryExecute(state, player, rules, kvp.Value);
        }
    }

    private static void TryExecute(OverworldSimState state, SimEntityState player, in OverworldSimRules rules, SimAbilityDefinition ability)
    {
        var castVfxCode = ComputeVfxCode(ability);
        if (GetCooldownTicks(player, ability.Slot) > 0)
        {
            SetCastFeedback(player, ability.Slot, CastResultCooldown, ability.TargetTeam, 0, castVfxCode);
            return;
        }

        if (ability.RequiredSpenderCostMilli > 0 && player.SpenderResource < ability.RequiredSpenderCostMilli)
        {
            SetCastFeedback(player, ability.Slot, CastResultInsufficientResource, ability.TargetTeam, 0, castVfxCode);
            return;
        }

        var consumedStatusStacks = 0;
        var targetEnemies = new List<SimEntityState>(8);
        var prevalidatedTargets = false;

        if (AbilityRequiresTargets(ability))
        {
            ResolveTargets(state, player, ability, targetEnemies);
            if (targetEnemies.Count == 0)
            {
                SetCastFeedback(player, ability.Slot, CastResultNoTarget, ability.TargetTeam, 0, castVfxCode);
                return;
            }

            prevalidatedTargets = true;
        }

        if (ability.Effects.Count == 0)
        {
            // Legacy fallback for builtin abilities.
            if (ability.HasDamageEffect)
            {
                var primaryEnemy = OverworldSimulator.FindNearestEnemyInRange(state, player.PositionXMilli, player.PositionYMilli, ability.RangeMilli);
                if (primaryEnemy is not null)
                {
                    OverworldSimulator.ApplyDamage(state, primaryEnemy, ComputeDamage(ability, player), player, blockedByGuard: false);
                }
            }

            if (ability.BuilderGainMilli > 0)
            {
                player.BuilderResource = Math.Clamp(
                    player.BuilderResource + ability.BuilderGainMilli,
                    0,
                    player.Character.DerivedStats.MaxBuilderResource * 1000);
            }

            if (ability.SpenderCostMilli > 0)
            {
                player.SpenderResource -= ability.SpenderCostMilli;
            }
        }
        else
        {
            for (var i = 0; i < ability.Effects.Count; i++)
            {
                var effect = ability.Effects[i];
                switch (effect.Primitive)
                {
                    case SimAbilityPrimitive.SpendResource:
                        {
                            if (effect.Amount > 0)
                            {
                                player.SpenderResource = Math.Max(0, player.SpenderResource - effect.Amount);
                            }

                            break;
                        }
                    case SimAbilityPrimitive.GainResource:
                        {
                            if (effect.Amount > 0)
                            {
                                player.BuilderResource = Math.Clamp(
                                    player.BuilderResource + effect.Amount,
                                    0,
                                    player.Character.DerivedStats.MaxBuilderResource * 1000);
                            }

                            break;
                        }
                    case SimAbilityPrimitive.DealDamage:
                        {
                            if (!prevalidatedTargets)
                            {
                                ResolveTargets(state, player, ability, targetEnemies);
                            }
                            if (targetEnemies.Count == 0)
                            {
                                break;
                            }

                            var computed = ComputeDamage(ability, player);
                            if (effect.CoefficientPermille > 0)
                            {
                                computed = computed * effect.CoefficientPermille / 1000;
                            }

                            computed += effect.Flat;
                            if (consumedStatusStacks > 0)
                            {
                                // Status-consume payoff baseline until full effect scripting lands.
                                computed += consumedStatusStacks * 3;
                            }

                            for (var targetIndex = 0; targetIndex < targetEnemies.Count; targetIndex++)
                            {
                                OverworldSimulator.ApplyDamage(state, targetEnemies[targetIndex], computed, player, blockedByGuard: false);
                            }
                            break;
                        }
                    case SimAbilityPrimitive.HitscanTrace:
                    case SimAbilityPrimitive.FireProjectile:
                        {
                            if (!prevalidatedTargets)
                            {
                                ResolveTargets(state, player, ability, targetEnemies);
                            }
                            if (targetEnemies.Count == 0)
                            {
                                break;
                            }

                            var computed = ComputeDamage(ability, player);
                            if (effect.CoefficientPermille > 0)
                            {
                                computed = computed * effect.CoefficientPermille / 1000;
                            }

                            computed += effect.Flat;
                            if (consumedStatusStacks > 0)
                            {
                                computed += consumedStatusStacks * 3;
                            }

                            // Trace/projectile are resolved authoritatively as deterministic multi-target hits for now.
                            for (var targetIndex = 0; targetIndex < targetEnemies.Count; targetIndex++)
                            {
                                OverworldSimulator.ApplyDamage(state, targetEnemies[targetIndex], computed, player, blockedByGuard: false);
                            }

                            break;
                        }
                    case SimAbilityPrimitive.ApplyShield:
                        {
                            var shieldAmount = effect.Amount > 0 ? effect.Amount : 16_000;
                            player.ShieldMilli = Math.Min(120_000, player.ShieldMilli + shieldAmount);
                            break;
                        }
                    case SimAbilityPrimitive.Heal:
                        {
                            if (!prevalidatedTargets)
                            {
                                ResolveTargets(state, player, ability, targetEnemies);
                            }
                            if (targetEnemies.Count == 0)
                            {
                                break;
                            }

                            var heal = effect.Flat;
                            if (heal <= 0)
                            {
                                heal = 8;
                            }

                            if (effect.CoefficientPermille > 0)
                            {
                                heal += player.Character.DerivedStats.SkillPotencyPermille * effect.CoefficientPermille / 100000;
                            }

                            for (var targetIndex = 0; targetIndex < targetEnemies.Count; targetIndex++)
                            {
                                var target = targetEnemies[targetIndex];
                                var maxHealth = target.Character.DerivedStats.MaxHealth;
                                target.Health = Math.Clamp(target.Health + heal, 0, maxHealth);
                            }

                            break;
                        }
                    case SimAbilityPrimitive.ApplyDamageReduction:
                        {
                            var reductionPercent = effect.Amount > 0 ? Math.Min(effect.Amount, 90) : 20;
                            var reductionPermille = reductionPercent * 10;
                            if (reductionPermille > player.DamageReductionPermille)
                            {
                                player.DamageReductionPermille = reductionPermille;
                            }

                            player.DamageReductionTicks = Math.Max(player.DamageReductionTicks, 120);
                            break;
                        }
                    case SimAbilityPrimitive.ApplyStatus:
                        {
                            if (!prevalidatedTargets)
                            {
                                ResolveTargets(state, player, ability, targetEnemies);
                            }
                            if (targetEnemies.Count == 0 || string.IsNullOrWhiteSpace(effect.StatusId))
                            {
                                break;
                            }

                            for (var targetIndex = 0; targetIndex < targetEnemies.Count; targetIndex++)
                            {
                                OverworldSimulator.ApplyStatus(targetEnemies[targetIndex], effect.StatusId!, 1, 300);
                            }
                            break;
                        }
                    case SimAbilityPrimitive.ConsumeStatus:
                        {
                            if (!prevalidatedTargets)
                            {
                                ResolveTargets(state, player, ability, targetEnemies);
                            }
                            if (targetEnemies.Count == 0 || string.IsNullOrWhiteSpace(effect.StatusId))
                            {
                                break;
                            }

                            var maxConsume = effect.Amount > 0 ? effect.Amount : 3;
                            for (var targetIndex = 0; targetIndex < targetEnemies.Count; targetIndex++)
                            {
                                var consumed = OverworldSimulator.ConsumeStatus(targetEnemies[targetIndex], effect.StatusId!, maxConsume);
                                if (consumed > consumedStatusStacks)
                                {
                                    consumedStatusStacks = consumed;
                                }
                            }
                            break;
                        }
                    case SimAbilityPrimitive.Cleanse:
                        {
                            OverworldSimulator.Cleanse(player);
                            break;
                        }
                    case SimAbilityPrimitive.Taunt:
                        {
                            if (!prevalidatedTargets)
                            {
                                ResolveTargets(state, player, ability, targetEnemies);
                            }
                            if (targetEnemies.Count == 0)
                            {
                                break;
                            }

                            for (var targetIndex = 0; targetIndex < targetEnemies.Count; targetIndex++)
                            {
                                OverworldSimulator.ApplyTaunt(state, targetEnemies[targetIndex], player.EntityId, 120);
                            }
                            break;
                        }
                    case SimAbilityPrimitive.AddThreat:
                        {
                            if (!prevalidatedTargets)
                            {
                                ResolveTargets(state, player, ability, targetEnemies);
                            }
                            if (targetEnemies.Count == 0)
                            {
                                break;
                            }

                            var threatAmount = effect.Amount > 0 ? effect.Amount : 1000;
                            for (var targetIndex = 0; targetIndex < targetEnemies.Count; targetIndex++)
                            {
                                OverworldSimulator.ApplyThreat(targetEnemies[targetIndex], player.EntityId, threatAmount);
                            }

                            break;
                        }
                    case SimAbilityPrimitive.ApplyCc:
                        {
                            if (!prevalidatedTargets)
                            {
                                ResolveTargets(state, player, ability, targetEnemies);
                            }
                            if (targetEnemies.Count == 0)
                            {
                                break;
                            }

                            for (var targetIndex = 0; targetIndex < targetEnemies.Count; targetIndex++)
                            {
                                OverworldSimulator.ApplyStatus(targetEnemies[targetIndex], "status.generic.slow", 1, 90);
                            }
                            break;
                        }
                    case SimAbilityPrimitive.SpawnZone:
                        {
                            if (string.IsNullOrWhiteSpace(effect.ZoneDefId))
                            {
                                break;
                            }

                            OverworldSimulator.TrySpawnZone(state, player, effect.ZoneDefId!, player.PositionXMilli, player.PositionYMilli);
                            break;
                        }
                    case SimAbilityPrimitive.CreateLink:
                        {
                            if (string.IsNullOrWhiteSpace(effect.LinkDefId))
                            {
                                break;
                            }

                            ResolveTargets(state, player, ability, targetEnemies);
                            if (targetEnemies.Count == 0)
                            {
                                var fallbackTarget = OverworldSimulator.FindNearestEnemyInRange(
                                    state,
                                    player.PositionXMilli,
                                    player.PositionYMilli,
                                    rules.WorldBoundaryMilli);
                                if (fallbackTarget is not null)
                                {
                                    targetEnemies.Add(fallbackTarget);
                                }
                            }

                            if (targetEnemies.Count == 0)
                            {
                                break;
                            }

                            for (var targetIndex = 0; targetIndex < targetEnemies.Count; targetIndex++)
                            {
                                OverworldSimulator.TryCreateLink(state, player, targetEnemies[targetIndex], effect.LinkDefId!);
                            }

                            break;
                        }
                    case SimAbilityPrimitive.BreakLink:
                        {
                            OverworldSimulator.BreakLinksOwnedBy(state, player.EntityId);
                            break;
                        }
                }
            }
        }

        if (consumedStatusStacks > 0)
        {
            player.DebugLastConsumedStatusStacks = consumedStatusStacks;
            player.DebugLastConsumedStatusTicks = 60;
        }

        var cooldownTicks = OverworldSimulator.ScaleByAttackSpeed(
            ability.BaseCooldownTicks,
            player.Character.DerivedStats.AttackSpeedPermille,
            ability.CooldownMinTicks);

        var affectedCount = ability.TargetTeam == SimTargetTeam.Self ? 1 : Math.Clamp(targetEnemies.Count, 0, byte.MaxValue);
        SetCooldownTicks(player, ability.Slot, cooldownTicks);
        SetCastFeedback(player, ability.Slot, CastResultSuccess, ability.TargetTeam, affectedCount, castVfxCode);
    }

    private static bool AbilityRequiresTargets(SimAbilityDefinition ability)
    {
        if (ability.Effects.Count == 0 && ability.HasDamageEffect && ability.TargetTeam == SimTargetTeam.Enemy)
        {
            return true;
        }

        for (var i = 0; i < ability.Effects.Count; i++)
        {
            switch (ability.Effects[i].Primitive)
            {
                case SimAbilityPrimitive.DealDamage:
                case SimAbilityPrimitive.HitscanTrace:
                case SimAbilityPrimitive.FireProjectile:
                case SimAbilityPrimitive.Heal:
                case SimAbilityPrimitive.ApplyStatus:
                case SimAbilityPrimitive.ConsumeStatus:
                case SimAbilityPrimitive.Taunt:
                case SimAbilityPrimitive.AddThreat:
                case SimAbilityPrimitive.ApplyCc:
                    return true;
            }
        }

        return false;
    }

    private static void SetCastFeedback(
        SimEntityState player,
        SimAbilitySlot slot,
        byte result,
        SimTargetTeam targetTeam,
        int affectedCount,
        ushort vfxCode)
    {
        player.DebugLastCastSlotCode = (byte)slot;
        player.DebugLastCastResultCode = result;
        player.DebugLastCastTargetTeamCode = (byte)targetTeam;
        player.DebugLastCastAffectedCount = Math.Clamp(affectedCount, 0, byte.MaxValue);
        player.DebugLastCastVfxCode = vfxCode;
        player.DebugLastCastFeedbackTicks = 45;
    }

    private static ushort ComputeVfxCode(SimAbilityDefinition ability)
    {
        var source = string.IsNullOrWhiteSpace(ability.VfxTag) ? ability.Id : ability.VfxTag;
        if (string.IsNullOrWhiteSpace(source))
        {
            return 0;
        }

        var hash = 2166136261u;
        for (var i = 0; i < source.Length; i++)
        {
            hash ^= source[i];
            hash *= 16777619u;
        }

        return (ushort)(hash & 0xFFFF);
    }

    private static int ComputeDamage(SimAbilityDefinition ability, SimEntityState player)
    {
        if (ability.Slot == SimAbilitySlot.Lmb)
        {
            var baseDamage = player.Character.DerivedStats.BaseMeleeDamage;
            return baseDamage * ability.DamageCoefficientPermille / 1000 + ability.DamageFlat;
        }

        if (ability.Slot == SimAbilitySlot.Rmb)
        {
            var baseDamage = player.Character.DerivedStats.BaseMeleeDamage * 18 / 10;
            return baseDamage * ability.DamageCoefficientPermille / 1000 + ability.DamageFlat;
        }

        var skillIndex = GetSkillIndex(ability.Slot);
        if (skillIndex >= 0)
        {
            var potency = player.Character.DerivedStats.SkillPotencyPermille;
            var baseDamage = (player.Character.DerivedStats.BaseMeleeDamage + 6 + skillIndex * 2) * potency / 1000;
            return baseDamage * ability.DamageCoefficientPermille / 1000 + ability.DamageFlat;
        }

        return 0;
    }

    private static void ResolveTargets(OverworldSimState state, SimEntityState player, SimAbilityDefinition ability, List<SimEntityState> targets)
    {
        targets.Clear();
        var maxTargets = ability.MaxTargets <= 0 ? 1 : ability.MaxTargets;
        switch (ability.TargetTeam)
        {
            case SimTargetTeam.Self:
                targets.Add(player);
                break;
            case SimTargetTeam.Ally:
                OverworldSimulator.FindPlayersInRange(
                    state,
                    player.PositionXMilli,
                    player.PositionYMilli,
                    ability.RangeMilli,
                    maxTargets + 1,
                    targets);
                var hadSelf = false;
                for (var i = targets.Count - 1; i >= 0; i--)
                {
                    if (targets[i].EntityId == player.EntityId)
                    {
                        hadSelf = true;
                        targets.RemoveAt(i);
                    }
                }

                if (targets.Count > maxTargets)
                {
                    targets.RemoveRange(maxTargets, targets.Count - maxTargets);
                }

                if (targets.Count == 0 && hadSelf)
                {
                    targets.Add(player);
                }
                else if (targets.Count < maxTargets && hadSelf)
                {
                    targets.Add(player);
                }
                break;
            case SimTargetTeam.Any:
                OverworldSimulator.FindAnyInRange(state, player.PositionXMilli, player.PositionYMilli, ability.RangeMilli, maxTargets, targets);
                break;
            default:
                OverworldSimulator.FindEnemiesInRange(state, player.PositionXMilli, player.PositionYMilli, ability.RangeMilli, maxTargets, targets);
                break;
        }
    }

    private static int GetCooldownTicks(SimEntityState player, SimAbilitySlot slot)
    {
        return slot switch
        {
            SimAbilitySlot.Lmb => player.FastAttackCooldownTicks,
            SimAbilitySlot.Rmb => player.HeavyAttackCooldownTicks,
            SimAbilitySlot.E => player.SkillCooldownTicks[0],
            SimAbilitySlot.R => player.SkillCooldownTicks[1],
            SimAbilitySlot.Q => player.SkillCooldownTicks[2],
            SimAbilitySlot.T => player.SkillCooldownTicks[3],
            SimAbilitySlot.Skill1 => player.SkillCooldownTicks[4],
            SimAbilitySlot.Skill2 => player.SkillCooldownTicks[5],
            SimAbilitySlot.Skill3 => player.SkillCooldownTicks[6],
            SimAbilitySlot.Skill4 => player.SkillCooldownTicks[7],
            _ => 0
        };
    }

    private static void SetCooldownTicks(SimEntityState player, SimAbilitySlot slot, int ticks)
    {
        switch (slot)
        {
            case SimAbilitySlot.Lmb:
                player.FastAttackCooldownTicks = ticks;
                break;
            case SimAbilitySlot.Rmb:
                player.HeavyAttackCooldownTicks = ticks;
                break;
            case SimAbilitySlot.E:
                player.SkillCooldownTicks[0] = ticks;
                break;
            case SimAbilitySlot.R:
                player.SkillCooldownTicks[1] = ticks;
                break;
            case SimAbilitySlot.Q:
                player.SkillCooldownTicks[2] = ticks;
                break;
            case SimAbilitySlot.T:
                player.SkillCooldownTicks[3] = ticks;
                break;
            case SimAbilitySlot.Skill1:
                player.SkillCooldownTicks[4] = ticks;
                break;
            case SimAbilitySlot.Skill2:
                player.SkillCooldownTicks[5] = ticks;
                break;
            case SimAbilitySlot.Skill3:
                player.SkillCooldownTicks[6] = ticks;
                break;
            case SimAbilitySlot.Skill4:
                player.SkillCooldownTicks[7] = ticks;
                break;
        }
    }

    private static int GetSkillIndex(SimAbilitySlot slot)
    {
        return slot switch
        {
            SimAbilitySlot.E => 0,
            SimAbilitySlot.R => 1,
            SimAbilitySlot.Q => 2,
            SimAbilitySlot.T => 3,
            SimAbilitySlot.Skill1 => 4,
            SimAbilitySlot.Skill2 => 5,
            SimAbilitySlot.Skill3 => 6,
            SimAbilitySlot.Skill4 => 7,
            _ => -1
        };
    }
}
