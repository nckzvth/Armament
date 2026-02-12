#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Armament.SharedSim.Sim;

public sealed class SimSpecContent
{
    public string Id { get; set; } = string.Empty;
    public string ClassId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string CanonicalStatusId { get; set; } = string.Empty;
    public Dictionary<string, string> Slots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SimAbilityContent
{
    public string Id { get; set; } = string.Empty;
    public string SpecId { get; set; } = string.Empty;
    public string Slot { get; set; } = string.Empty;
    public string InputBehavior { get; set; } = "tap";
    public string AnimTag { get; set; } = string.Empty;
    public string VfxTag { get; set; } = string.Empty;
    public int CooldownMs { get; set; }
    public SimTargetingContent Targeting { get; set; } = new();
    public List<SimEffectContent> Effects { get; set; } = new();
}

public sealed class SimTargetingContent
{
    public string Type { get; set; } = "self";
    public decimal RangeM { get; set; }
    public string TeamFilter { get; set; } = "enemy";
    public int MaxTargets { get; set; } = 1;
}

public sealed class SimEffectContent
{
    public string Primitive { get; set; } = string.Empty;
    public string? StatusId { get; set; }
    public string? ZoneDefId { get; set; }
    public string? LinkDefId { get; set; }
    public string? ProjectileDefId { get; set; }
    public string? TraceDefId { get; set; }
    public int? Amount { get; set; }
    public int? CoefficientPermille { get; set; }
    public int? Flat { get; set; }
}

public static class AbilityProfileCompiler
{
    public static bool TryCompile(
        SimSpecContent spec,
        IReadOnlyDictionary<string, SimAbilityContent> abilitiesById,
        OverworldSimRules rules,
        out SimAbilityProfile profile,
        out string error)
    {
        profile = new SimAbilityProfile { Id = spec.Id };
        error = string.Empty;

        foreach (var slotMap in spec.Slots)
        {
            if (!TryParseSlot(slotMap.Key, out var slot))
            {
                error = $"unsupported slot '{slotMap.Key}' in spec '{spec.Id}'";
                return false;
            }

            if (!abilitiesById.TryGetValue(slotMap.Value, out var content))
            {
                error = $"missing ability '{slotMap.Value}' for slot '{slotMap.Key}'";
                return false;
            }

            if (!string.Equals(content.SpecId, spec.Id, StringComparison.Ordinal))
            {
                error = $"ability '{content.Id}' belongs to '{content.SpecId}', expected '{spec.Id}'";
                return false;
            }

            if (!TryParseInputBehavior(content.InputBehavior, out var behavior))
            {
                error = $"ability '{content.Id}' has unsupported input behavior '{content.InputBehavior}'";
                return false;
            }

            var hasDamage = content.Effects.Any(x => string.Equals(x.Primitive, "DealDamage", StringComparison.OrdinalIgnoreCase));
            var spendEffect = content.Effects.FirstOrDefault(x => string.Equals(x.Primitive, "SpendResource", StringComparison.OrdinalIgnoreCase));
            var gainEffect = content.Effects.FirstOrDefault(x => string.Equals(x.Primitive, "GainResource", StringComparison.OrdinalIgnoreCase));
            var damageEffect = content.Effects.FirstOrDefault(x => string.Equals(x.Primitive, "DealDamage", StringComparison.OrdinalIgnoreCase));

            var baseCooldownTicks = Math.Max(0, (int)Math.Ceiling(content.CooldownMs / (1000m / rules.SimulationHz)));
            var rangeMilli = (int)Math.Round(content.Targeting.RangeM * 1000m, MidpointRounding.AwayFromZero);
            if (rangeMilli <= 0)
            {
                rangeMilli = ResolveDefaultRange(slot, rules);
            }

            var spenderCostMilli = ResolveDefaultSpenderCost(slot, rules);
            if (spendEffect?.Amount is > 0)
            {
                spenderCostMilli = spendEffect.Amount.Value * 1000;
            }
            else if (spendEffect is null && !UsesLegacySpenderCost(slot))
            {
                spenderCostMilli = 0;
            }

            var builderGainMilli = 0;
            if (gainEffect is not null)
            {
                builderGainMilli = gainEffect.Amount is > 0
                    ? gainEffect.Amount.Value * 1000
                    : ResolveDefaultBuilderGain(slot, rules);
            }

            var damageCoefficientPermille = damageEffect?.CoefficientPermille ?? 1000;
            if (damageCoefficientPermille <= 0)
            {
                damageCoefficientPermille = 1000;
            }

            var damageFlat = damageEffect?.Flat ?? 0;

            var definition = new SimAbilityDefinition
            {
                Id = content.Id,
                Slot = slot,
                InputBehavior = behavior,
                AnimTag = content.AnimTag ?? string.Empty,
                VfxTag = content.VfxTag ?? string.Empty,
                BaseCooldownTicks = baseCooldownTicks,
                CooldownMinTicks = ResolveDefaultMinCooldown(slot),
                RangeMilli = rangeMilli,
                SpenderCostMilli = spenderCostMilli,
                BuilderGainMilli = builderGainMilli,
                HasDamageEffect = hasDamage || UsesLegacyDamageFallback(slot),
                DamageCoefficientPermille = damageCoefficientPermille,
                DamageFlat = damageFlat,
                MaxTargets = content.Targeting.MaxTargets <= 0 ? 1 : content.Targeting.MaxTargets,
                TargetTeam = ResolveTargetTeam(content.Targeting)
            };

            var requiredSpend = 0;
            for (var i = 0; i < content.Effects.Count; i++)
            {
                var source = content.Effects[i];
                if (!TryMapPrimitive(source.Primitive, out var primitive))
                {
                    continue;
                }

                var mapped = new SimAbilityEffect
                {
                    Primitive = primitive,
                    StatusId = source.StatusId,
                    ZoneDefId = source.ZoneDefId,
                    LinkDefId = source.LinkDefId,
                    Amount = source.Amount.GetValueOrDefault() * 1000,
                    CoefficientPermille = source.CoefficientPermille.GetValueOrDefault(1000),
                    Flat = source.Flat.GetValueOrDefault()
                };

                if (mapped.Primitive == SimAbilityPrimitive.SpendResource)
                {
                    var spend = source.Amount.GetValueOrDefault();
                    if (spend <= 0)
                    {
                        spend = ResolveDefaultSpenderCost(slot, rules) / 1000;
                    }

                    mapped.Amount = spend * 1000;
                    requiredSpend += mapped.Amount;
                }
                else if (mapped.Primitive == SimAbilityPrimitive.GainResource)
                {
                    var gain = source.Amount.GetValueOrDefault();
                    if (gain <= 0)
                    {
                        gain = ResolveDefaultBuilderGain(slot, rules);
                    }

                    mapped.Amount = gain * 1000;
                }
                else if (mapped.Primitive == SimAbilityPrimitive.ApplyShield)
                {
                    // Default fixed shield if authoring omits amount.
                    mapped.Amount = source.Amount.GetValueOrDefault(16) * 1000;
                }
                else if (mapped.Primitive == SimAbilityPrimitive.ApplyDamageReduction)
                {
                    // Amount is percent in content.
                    mapped.Amount = source.Amount.GetValueOrDefault(20);
                }
                else if (mapped.Primitive == SimAbilityPrimitive.ConsumeStatus)
                {
                    // Amount is max stacks to consume.
                    mapped.Amount = source.Amount.GetValueOrDefault(3);
                }

                definition.Effects.Add(mapped);
            }

            if (definition.Effects.Count == 0)
            {
                // Preserve legacy fallback behavior when content is incomplete.
                if (spenderCostMilli > 0)
                {
                    definition.Effects.Add(new SimAbilityEffect
                    {
                        Primitive = SimAbilityPrimitive.SpendResource,
                        Amount = spenderCostMilli
                    });
                    requiredSpend += spenderCostMilli;
                }

                if (builderGainMilli > 0)
                {
                    definition.Effects.Add(new SimAbilityEffect
                    {
                        Primitive = SimAbilityPrimitive.GainResource,
                        Amount = builderGainMilli
                    });
                }

                if (definition.HasDamageEffect)
                {
                    definition.Effects.Add(new SimAbilityEffect
                    {
                        Primitive = SimAbilityPrimitive.DealDamage,
                        CoefficientPermille = definition.DamageCoefficientPermille,
                        Flat = definition.DamageFlat
                    });
                }
            }

            definition.RequiredSpenderCostMilli = requiredSpend;

            profile.AbilitiesByFlag[SimAbilityProfiles.MapTriggerFlag(slot)] = definition;
        }

        return true;
    }

    private static bool TryParseSlot(string slot, out SimAbilitySlot parsed)
    {
        parsed = slot.ToLowerInvariant() switch
        {
            "lmb" => SimAbilitySlot.Lmb,
            "rmb" => SimAbilitySlot.Rmb,
            "shift" => SimAbilitySlot.Shift,
            "e" => SimAbilitySlot.E,
            "r" => SimAbilitySlot.R,
            "q" => SimAbilitySlot.Q,
            "t" => SimAbilitySlot.T,
            "1" => SimAbilitySlot.Skill1,
            "2" => SimAbilitySlot.Skill2,
            "3" => SimAbilitySlot.Skill3,
            "4" => SimAbilitySlot.Skill4,
            _ => (SimAbilitySlot)255
        };

        return parsed != (SimAbilitySlot)255;
    }

    private static bool TryParseInputBehavior(string behavior, out SimAbilityInputBehavior parsed)
    {
        parsed = behavior.ToLowerInvariant() switch
        {
            "tap" => SimAbilityInputBehavior.Tap,
            "hold_repeat" => SimAbilityInputBehavior.HoldRepeat,
            "hold_release_charge" => SimAbilityInputBehavior.HoldReleaseCharge,
            _ => (SimAbilityInputBehavior)255
        };

        return parsed != (SimAbilityInputBehavior)255;
    }

    private static int ResolveDefaultRange(SimAbilitySlot slot, OverworldSimRules rules)
    {
        return slot switch
        {
            SimAbilitySlot.Lmb => rules.MeleeRangeMilli,
            SimAbilitySlot.Rmb => rules.MeleeRangeMilli + 300,
            SimAbilitySlot.Shift => 0,
            _ => rules.SkillRangeMilli
        };
    }

    private static int ResolveDefaultMinCooldown(SimAbilitySlot slot)
    {
        return slot switch
        {
            SimAbilitySlot.Lmb => 4,
            SimAbilitySlot.Rmb => 8,
            SimAbilitySlot.Shift => 1,
            _ => 20
        };
    }

    private static int ResolveDefaultSpenderCost(SimAbilitySlot slot, OverworldSimRules rules)
    {
        return slot switch
        {
            SimAbilitySlot.Rmb => rules.HeavyAttackSpenderCost * 1000,
            SimAbilitySlot.E or SimAbilitySlot.R or SimAbilitySlot.Q or SimAbilitySlot.T or SimAbilitySlot.Skill1 or SimAbilitySlot.Skill2 or SimAbilitySlot.Skill3 or SimAbilitySlot.Skill4
                => rules.SkillSpenderCost * 1000,
            _ => 0
        };
    }

    private static int ResolveDefaultBuilderGain(SimAbilitySlot slot, OverworldSimRules rules)
    {
        return slot switch
        {
            SimAbilitySlot.Lmb => rules.FastAttackBuilderGain * 1000,
            _ => 0
        };
    }

    private static bool UsesLegacySpenderCost(SimAbilitySlot slot)
    {
        return slot switch
        {
            SimAbilitySlot.Rmb => true,
            SimAbilitySlot.E => true,
            SimAbilitySlot.R => true,
            SimAbilitySlot.Q => true,
            SimAbilitySlot.T => true,
            SimAbilitySlot.Skill1 => true,
            SimAbilitySlot.Skill2 => true,
            SimAbilitySlot.Skill3 => true,
            SimAbilitySlot.Skill4 => true,
            _ => false
        };
    }

    private static bool UsesLegacyDamageFallback(SimAbilitySlot slot)
    {
        return slot != SimAbilitySlot.Shift;
    }

    private static bool TryMapPrimitive(string primitive, out SimAbilityPrimitive mapped)
    {
        mapped = primitive.ToLowerInvariant() switch
        {
            "startcooldown" => SimAbilityPrimitive.StartCooldown,
            "spendresource" => SimAbilityPrimitive.SpendResource,
            "gainresource" => SimAbilityPrimitive.GainResource,
            "dealdamage" => SimAbilityPrimitive.DealDamage,
            "applyshield" => SimAbilityPrimitive.ApplyShield,
            "applydr" => SimAbilityPrimitive.ApplyDamageReduction,
            "applystatus" => SimAbilityPrimitive.ApplyStatus,
            "consumestatus" => SimAbilityPrimitive.ConsumeStatus,
            "cleanse" => SimAbilityPrimitive.Cleanse,
            "taunt" => SimAbilityPrimitive.Taunt,
            "applycc" => SimAbilityPrimitive.ApplyCc,
            "spawnzone" => SimAbilityPrimitive.SpawnZone,
            "hitscantrace" => SimAbilityPrimitive.HitscanTrace,
            "fireprojectile" => SimAbilityPrimitive.FireProjectile,
            "addthreat" => SimAbilityPrimitive.AddThreat,
            "createlink" => SimAbilityPrimitive.CreateLink,
            "breaklink" => SimAbilityPrimitive.BreakLink,
            "heal" => SimAbilityPrimitive.Heal,
            _ => SimAbilityPrimitive.None
        };

        return mapped != SimAbilityPrimitive.None;
    }

    private static SimTargetTeam ResolveTargetTeam(SimTargetingContent targeting)
    {
        if (targeting.Type.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            return SimTargetTeam.Self;
        }

        return targeting.TeamFilter.ToLowerInvariant() switch
        {
            "ally" => SimTargetTeam.Ally,
            "any" => SimTargetTeam.Any,
            _ => SimTargetTeam.Enemy
        };
    }
}
