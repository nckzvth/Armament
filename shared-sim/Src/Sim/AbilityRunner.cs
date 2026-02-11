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
    public int BaseCooldownTicks { get; set; }
    public int CooldownMinTicks { get; set; }
    public int RangeMilli { get; set; }
    public int SpenderCostMilli { get; set; }
    public int BuilderGainMilli { get; set; }
    public bool HasDamageEffect { get; set; } = true;
    public int DamageCoefficientPermille { get; set; } = 1000;
    public int DamageFlat { get; set; }
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
        if (GetCooldownTicks(player, ability.Slot) > 0)
        {
            return;
        }

        if (ability.SpenderCostMilli > 0 && player.SpenderResource < ability.SpenderCostMilli)
        {
            return;
        }

        if (ability.HasDamageEffect)
        {
            var enemy = OverworldSimulator.FindNearestEnemyInRange(
                state,
                player.PositionXMilli,
                player.PositionYMilli,
                ability.RangeMilli);

            if (enemy is not null)
            {
                OverworldSimulator.ApplyDamage(state, enemy, ComputeDamage(ability, player), player);
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

        var cooldownTicks = OverworldSimulator.ScaleByAttackSpeed(
            ability.BaseCooldownTicks,
            player.Character.DerivedStats.AttackSpeedPermille,
            ability.CooldownMinTicks);

        SetCooldownTicks(player, ability.Slot, cooldownTicks);
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
