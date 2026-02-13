using Armament.SharedSim.Determinism;
using Armament.SharedSim.Inventory;
using Armament.SharedSim.Protocol;
using Armament.SharedSim.Sim;

var failures = new List<string>();

void Assert(bool condition, string message)
{
    if (!condition)
    {
        failures.Add(message);
    }
}

void ProtocolRoundtripAssertions()
{
    var snapshot = new WorldSnapshot
    {
        ServerTick = 42,
        LastProcessedInputSequence = 19,
        WorldObjects =
        {
            new WorldObjectSnapshot
            {
                ObjectId = 7001,
                ObjectDefId = "obj.bell_pylon",
                Archetype = "objective",
                EncounterId = "enc.ig.disable_bell_pylons",
                QuantizedX = 120,
                QuantizedY = -30,
                Health = 120,
                MaxHealth = 220,
                ObjectiveState = 1
            }
        },
        Hazards =
        {
            new WorldHazardSnapshot
            {
                HazardRuntimeId = 8101,
                HazardId = "haz.bell_pulse",
                EncounterId = "enc.ig.disable_bell_pylons",
                QuantizedX = 120,
                QuantizedY = -30,
                RemainingTicks = 45,
                ObjectiveState = 1
            }
        },
        Npcs =
        {
            new WorldNpcSnapshot
            {
                NpcRuntimeId = 9101,
                NpcId = "npc.quartermaster",
                ZoneId = "zone.cp",
                Name = "Quartermaster",
                QuantizedX = 40,
                QuantizedY = -10,
                InteractRadiusDeciUnits = 20,
                ObjectiveState = 1
            }
        },
        Objectives =
        {
            new WorldObjectiveSnapshot
            {
                ObjectiveId = "quest.act1.silence_inner_graveyard:obj:0",
                EncounterId = "enc.ig.disable_bell_pylons",
                Kind = "DestroyObjectType",
                TargetId = "obj.bell_pylon",
                Current = 1,
                Required = 2,
                State = 1
            }
        },
        Entities =
        {
            new EntitySnapshot
            {
                EntityId = 1,
                Kind = EntityKind.Player,
                QuantizedX = 150,
                QuantizedY = -200,
                Health = 80,
                BuilderResource = 17,
                SpenderResource = 22,
                Currency = 4
            },
            new EntitySnapshot
            {
                EntityId = 2,
                Kind = EntityKind.Enemy,
                QuantizedX = -30,
                QuantizedY = 90,
                Health = 100,
                BuilderResource = 0,
                SpenderResource = 0,
                Currency = 0,
                DebugPrimaryStatusStacks = 2,
                DebugConsumedStatusStacks = 3,
                DebugLastCastSlotCode = 9,
                DebugLastCastResultCode = 1
            }
        }
    };

    var encoded = ProtocolCodec.Encode(snapshot);
    Assert(ProtocolCodec.TryDecode(encoded, out var decodedMsg), "snapshot decode failed");
    var decoded = decodedMsg as WorldSnapshot;
    Assert(decoded is not null, "decoded message type mismatch");
    Assert(decoded!.Entities.Count == 2, "snapshot entity count mismatch");
    Assert(decoded.WorldObjects.Count == 1, "snapshot world object count mismatch");
    Assert(decoded.Hazards.Count == 1, "snapshot hazard count mismatch");
    Assert(decoded.Npcs.Count == 1, "snapshot npc count mismatch");
    Assert(decoded.Objectives.Count == 1, "snapshot objective count mismatch");
    Assert(decoded.LastProcessedInputSequence == 19, "snapshot ack mismatch");
    Assert(decoded.Entities[1].Kind == EntityKind.Enemy, "snapshot entity kind mismatch");
    Assert(decoded.Entities[1].DebugLastCastSlotCode == 9, "snapshot cast slot feedback mismatch");
    Assert(decoded.Entities[1].DebugLastCastResultCode == 1, "snapshot cast result feedback mismatch");

    var inputCmd = new InputCommand
    {
        Sequence = 9,
        ClientTick = 77,
        MoveX = Quantization.QuantizeInput(0.5f),
        MoveY = Quantization.QuantizeInput(-0.25f),
        ActionFlags = InputActionFlags.FastAttackHold | InputActionFlags.BlockHold
    };

    var inputPayload = ProtocolCodec.Encode(inputCmd);
    Assert(ProtocolCodec.TryDecode(inputPayload, out var decodedInputMessage), "input command decode failed");
    var decodedInput = decodedInputMessage as InputCommand;
    Assert(decodedInput is not null, "input command type mismatch");
    Assert(decodedInput!.ClientTick == 77, "input command client tick mismatch");
    Assert(decodedInput.ActionFlags.HasFlag(InputActionFlags.BlockHold), "input command flags mismatch");
}

void RngAssertions()
{
    var rngA = new XorShift32(1234);
    var rngB = new XorShift32(1234);
    for (var i = 0; i < 16; i++)
    {
        Assert(rngA.NextUInt() == rngB.NextUInt(), $"rng diverged at step {i}");
    }
}

void CharacterMathAssertions()
{
    var attributes = new CharacterAttributes
    {
        Might = 15,
        Will = 12,
        Alacrity = 8,
        Constitution = 11
    };

    var derived = CharacterMath.ComputeDerived(attributes, CharacterStatTuning.Default);

    Assert(derived.BaseMeleeDamage == 38, "might to melee damage mapping changed unexpectedly");
    Assert(derived.KnockbackPermille == 1120, "might to knockback mapping changed unexpectedly");
    Assert(derived.SkillPotencyPermille == 1072, "will to skill potency mapping changed unexpectedly");
    Assert(derived.ResourceRegenPermillePerSecond == 1084, "will to resource regen mapping changed unexpectedly");
    Assert(derived.AttackSpeedPermille == 1080, "alacrity to attack speed mapping changed unexpectedly");
    Assert(derived.MoveSpeedMilliPerSecond == 4368, "alacrity to movement mapping changed unexpectedly");
    Assert(derived.MaxHealth == 232, "constitution to health mapping changed unexpectedly");
}

OverworldSimState BuildCombatState()
{
    var tune = CharacterStatTuning.Default;

    var state = new OverworldSimState { Seed = 5 };
    var player = new SimEntityState
    {
        EntityId = 1,
        Kind = EntityKind.Player,
        PositionXMilli = 0,
        PositionYMilli = 0
    };
    player.Character.CharacterId = 101;
    player.Character.Attributes = CharacterAttributes.Default;
    player.Character.RecalculateDerivedStats(tune);
    player.Health = player.Character.DerivedStats.MaxHealth;
    player.SpenderResource = player.Character.DerivedStats.MaxSpenderResource * 1000;

    var enemy = new SimEntityState
    {
        EntityId = 2,
        Kind = EntityKind.Enemy,
        PositionXMilli = 1200,
        PositionYMilli = 0,
        Health = 90
    };
    enemy.Character.Attributes = new CharacterAttributes { Might = 9, Will = 5, Alacrity = 7, Constitution = 8 };
    enemy.Character.RecalculateDerivedStats(tune);

    state.UpsertEntity(player);
    state.UpsertEntity(enemy);
    return state;
}

void CombatAssertions()
{
    var rules = OverworldSimRules.Default;
    var state = BuildCombatState();

    state.TryGetEntity(1, out var player);
    state.TryGetEntity(2, out var enemy);

    var builderBefore = player!.BuilderResource;
    var enemyHpBefore = enemy!.Health;

    player.ActionFlags = InputActionFlags.FastAttackHold;
    OverworldSimulator.Step(state, rules);

    Assert(player.BuilderResource > builderBefore, "fast attack did not build builder resource");
    Assert(enemy.Health < enemyHpBefore, "fast attack did not damage enemy");

    var spenderBefore = player.SpenderResource;
    player.ActionFlags = InputActionFlags.HeavyAttackHold;
    for (var i = 0; i < 40; i++) OverworldSimulator.Step(state, rules);
    Assert(player.SpenderResource < spenderBefore, "heavy attack did not spend spender resource");

    state = BuildCombatState();
    state.TryGetEntity(1, out player);
    state.TryGetEntity(2, out enemy);

    player.ActionFlags = InputActionFlags.BlockHold;
    player.Health = player.Character.DerivedStats.MaxHealth;
    enemy.PositionXMilli = player.PositionXMilli + 500;
    enemy.PositionYMilli = player.PositionYMilli;
    enemy.EnemyAttackCooldownTicks = 0;
    OverworldSimulator.Step(state, rules);
    var blockedHealth = player.Health;

    player.ActionFlags = InputActionFlags.None;
    player.Health = player.Character.DerivedStats.MaxHealth;
    enemy.EnemyAttackCooldownTicks = 0;
    OverworldSimulator.Step(state, rules);
    var unblockedHealth = player.Health;

    Assert(blockedHealth > unblockedHealth, "block state did not mitigate enemy damage");
}

void LootPickupAssertions()
{
    var rules = OverworldSimRules.Default;
    var state = BuildCombatState();

    state.TryGetEntity(1, out var player);
    state.TryGetEntity(2, out var enemy);

    enemy!.Health = 1;
    player!.ActionFlags = InputActionFlags.FastAttackHold;
    OverworldSimulator.Step(state, rules);
    Assert(
        state.SimEvents.Any(e => e.Kind == SimEventKind.EnemyKilled && e.PlayerEntityId == player.EntityId && e.SubjectEntityId == enemy.EntityId),
        "enemy kill event was not emitted");

    var hasLoot = false;
    foreach (var loot in state.LootDrops.Values)
    {
        if (!loot.Claimed)
        {
            hasLoot = true;
            player.PositionXMilli = loot.PositionXMilli;
            player.PositionYMilli = loot.PositionYMilli;
        }
    }

    Assert(hasLoot, "enemy death did not spawn loot");
    var currencyBefore = player.Character.Currency;
    player.ActionFlags = InputActionFlags.Pickup;
    OverworldSimulator.Step(state, rules);
    Assert(player.Character.Currency > currencyBefore, "click/pickup flag did not grant gold currency");
    Assert(
        state.SimEvents.Any(e => e.Kind == SimEventKind.TokenCollected && e.PlayerEntityId == player.EntityId),
        "token collected event was not emitted for manual pickup");

    // Verify auto-loot still works without explicit pickup for auto-loot-enabled drops.
    state = BuildCombatState();
    state.TryGetEntity(1, out player);
    state.TryGetEntity(2, out enemy);
    enemy!.Health = 1;
    player!.ActionFlags = InputActionFlags.FastAttackHold;
    OverworldSimulator.Step(state, rules);

    foreach (var loot in state.LootDrops.Values)
    {
        if (!loot.Claimed)
        {
            player.PositionXMilli = loot.PositionXMilli;
            player.PositionYMilli = loot.PositionYMilli;
        }
    }

    currencyBefore = player.Character.Currency;
    player.ActionFlags = InputActionFlags.None;
    OverworldSimulator.Step(state, rules);

    Assert(player.Character.Currency > currencyBefore, "gold auto-loot did not trigger");
    Assert(
        state.SimEvents.Any(e => e.Kind == SimEventKind.TokenCollected && e.PlayerEntityId == player.EntityId),
        "token collected event was not emitted for auto loot");

    // Verify non-auto loot requires explicit pickup action.
    state = BuildCombatState();
    state.TryGetEntity(1, out player);
    var manualLoot = state.SpawnLoot(player!.PositionXMilli, player.PositionYMilli, currencyAmount: 7, autoLoot: false);
    currencyBefore = player.Character.Currency;

    player.ActionFlags = InputActionFlags.None;
    OverworldSimulator.Step(state, rules);
    Assert(player.Character.Currency == currencyBefore, "non-auto loot should not be collected without pickup flag");
    Assert(state.LootDrops.TryGetValue(manualLoot.LootId, out var remainedLoot) && !remainedLoot.Claimed, "non-auto loot was unexpectedly claimed");

    player.ActionFlags = InputActionFlags.Pickup;
    OverworldSimulator.Step(state, rules);
    Assert(player.Character.Currency == currencyBefore + 7, "pickup flag did not collect non-auto loot");
}

void DeterminismReplayAssertions()
{
    var rules = OverworldSimRules.Default;

    OverworldSimState BuildState()
    {
        var state = new OverworldSimState { Seed = 1337 };
        var tune = CharacterStatTuning.Default;
        for (uint i = 1; i <= 2; i++)
        {
            var entity = new SimEntityState
            {
                EntityId = i,
                Kind = i == 1 ? EntityKind.Player : EntityKind.Enemy,
                PositionXMilli = (int)i * 1000,
                PositionYMilli = 0,
                Health = 120
            };
            entity.Character.CharacterId = i;
            entity.Character.Attributes = CharacterAttributes.Default;
            entity.Character.RecalculateDerivedStats(tune);
            entity.SpenderResource = entity.Character.DerivedStats.MaxSpenderResource * 1000;
            state.UpsertEntity(entity);
        }

        return state;
    }

    var worldA = BuildState();
    var worldB = BuildState();
    var rngA = new XorShift32(9001);
    var rngB = new XorShift32(9001);

    for (var tick = 0; tick < 240; tick++)
    {
        foreach (var entityId in new uint[] { 1, 2 })
        {
            worldA.TryGetEntity(entityId, out var entityA);
            worldB.TryGetEntity(entityId, out var entityB);

            var ax = (short)(rngA.NextUInt() % 2001 - 1000);
            var ay = (short)(rngA.NextUInt() % 2001 - 1000);
            var bx = (short)(rngB.NextUInt() % 2001 - 1000);
            var by = (short)(rngB.NextUInt() % 2001 - 1000);

            entityA!.InputX = ax;
            entityA.InputY = ay;
            entityA.ActionFlags = (rngA.NextUInt() & 1) == 0 ? InputActionFlags.FastAttackHold : InputActionFlags.None;

            entityB!.InputX = bx;
            entityB.InputY = by;
            entityB.ActionFlags = (rngB.NextUInt() & 1) == 0 ? InputActionFlags.FastAttackHold : InputActionFlags.None;
        }

        OverworldSimulator.Step(worldA, rules);
        OverworldSimulator.Step(worldB, rules);
    }

    Assert(worldA.Tick == worldB.Tick, "world tick diverged under replay");
    Assert(worldA.ComputeWorldHash() == worldB.ComputeWorldHash(), "world hash diverged for identical seed and input stream");
}

void TauntAndCcAssertions()
{
    var rules = OverworldSimRules.Default;
    var tune = CharacterStatTuning.Default;

    var state = new OverworldSimState { Seed = 777 };
    var taunter = new SimEntityState
    {
        EntityId = 1,
        Kind = EntityKind.Player,
        PositionXMilli = -3200,
        PositionYMilli = 0
    };
    taunter.Character.CharacterId = 1;
    taunter.Character.Attributes = CharacterAttributes.Default;
    taunter.Character.RecalculateDerivedStats(tune);
    taunter.Health = taunter.Character.DerivedStats.MaxHealth;
    taunter.SpenderResource = taunter.Character.DerivedStats.MaxSpenderResource * 1000;

    var nearPlayer = new SimEntityState
    {
        EntityId = 2,
        Kind = EntityKind.Player,
        PositionXMilli = 0,
        PositionYMilli = 0
    };
    nearPlayer.Character.CharacterId = 2;
    nearPlayer.Character.Attributes = CharacterAttributes.Default;
    nearPlayer.Character.RecalculateDerivedStats(tune);
    nearPlayer.Health = nearPlayer.Character.DerivedStats.MaxHealth;
    nearPlayer.SpenderResource = nearPlayer.Character.DerivedStats.MaxSpenderResource * 1000;

    var enemy = new SimEntityState
    {
        EntityId = 3,
        Kind = EntityKind.Enemy,
        PositionXMilli = 1200,
        PositionYMilli = 0,
        Health = 100
    };
    enemy.Character.Attributes = CharacterAttributes.Default;
    enemy.Character.RecalculateDerivedStats(tune);

    state.UpsertEntity(taunter);
    state.UpsertEntity(nearPlayer);
    state.UpsertEntity(enemy);

    OverworldSimulator.ApplyTaunt(state, enemy, taunter.EntityId, 120);
    Assert(enemy.ForcedTargetEntityId == taunter.EntityId, "taunt did not bind enemy to taunter");
    Assert(enemy.ForcedTargetTicks >= 120, "taunt did not set forced target duration");

    var enemyXBefore = enemy.PositionXMilli;
    OverworldSimulator.Step(state, rules);
    Assert(enemy.PositionXMilli < enemyXBefore, "taunted enemy did not move toward forced target");

    var slowTarget = new SimEntityState
    {
        EntityId = 4,
        Kind = EntityKind.Enemy,
        PositionXMilli = 4200,
        PositionYMilli = 0,
        Health = 100
    };
    slowTarget.Character.Attributes = CharacterAttributes.Default;
    slowTarget.Character.RecalculateDerivedStats(tune);

    state.UpsertEntity(slowTarget);
    var baselineX = slowTarget.PositionXMilli;
    OverworldSimulator.Step(state, rules);
    var baselineDelta = baselineX - slowTarget.PositionXMilli;

    slowTarget.PositionXMilli = 4200;
    slowTarget.Statuses["status.generic.slow"] = new SimStatusState
    {
        Id = "status.generic.slow",
        Stacks = 1,
        RemainingTicks = 120
    };
    OverworldSimulator.Step(state, rules);
    var slowedDelta = 4200 - slowTarget.PositionXMilli;

    Assert(slowedDelta > 0, "slow target did not move");
    Assert(slowedDelta < baselineDelta, "slow status did not reduce enemy movement speed");
}

void ZoneSpawnAssertions()
{
    var rules = OverworldSimRules.Default;
    var state = BuildCombatState();
    state.TryGetEntity(1, out var player);
    state.TryGetEntity(2, out var enemy);

    var profile = new SimAbilityProfile { Id = "spec.test.zone" };
    profile.AbilitiesByFlag[InputActionFlags.Skill1] = new SimAbilityDefinition
    {
        Id = "ability.test.zone_e",
        Slot = SimAbilitySlot.E,
        InputBehavior = SimAbilityInputBehavior.Tap,
        BaseCooldownTicks = 10,
        CooldownMinTicks = 5,
        RangeMilli = 2500,
        HasDamageEffect = false,
        Effects =
        {
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.SpawnZone, ZoneDefId = "zone.exorcist.warden.abjuration_field" }
        }
    };
    state.RegisterAbilityProfile(profile);
    player!.AbilityProfileId = profile.Id;

    var hpBefore = enemy!.Health;
    player.ActionFlags = InputActionFlags.Skill1;
    OverworldSimulator.Step(state, rules);

    Assert(state.Zones.Count > 0, "spawn zone primitive did not create zone state");

    player.ActionFlags = InputActionFlags.None;
    for (var i = 0; i < 80; i++)
    {
        OverworldSimulator.Step(state, rules);
    }

    Assert(enemy.Health < hpBefore, "zone pulses did not damage enemy over time");
    Assert(enemy.Statuses.ContainsKey("status.exorcist.warden.bound"), "zone pulses did not apply bound status");
    Assert(player.DamageReductionPermille > 0, "ward zone did not grant player mitigation");
}

void MultiTargetAbilityAssertions()
{
    var rules = OverworldSimRules.Default;
    var tune = CharacterStatTuning.Default;
    var state = new OverworldSimState { Seed = 42 };

    var player = new SimEntityState
    {
        EntityId = 1,
        Kind = EntityKind.Player,
        PositionXMilli = 0,
        PositionYMilli = 0
    };
    player.Character.Attributes = CharacterAttributes.Default;
    player.Character.RecalculateDerivedStats(tune);
    player.Health = player.Character.DerivedStats.MaxHealth;
    player.SpenderResource = player.Character.DerivedStats.MaxSpenderResource * 1000;

    var enemyA = new SimEntityState
    {
        EntityId = 2,
        Kind = EntityKind.Enemy,
        PositionXMilli = 1200,
        PositionYMilli = 0,
        Health = 160
    };
    enemyA.Character.Attributes = CharacterAttributes.Default;
    enemyA.Character.RecalculateDerivedStats(tune);

    var enemyB = new SimEntityState
    {
        EntityId = 3,
        Kind = EntityKind.Enemy,
        PositionXMilli = 1400,
        PositionYMilli = 450,
        Health = 160
    };
    enemyB.Character.Attributes = CharacterAttributes.Default;
    enemyB.Character.RecalculateDerivedStats(tune);

    state.UpsertEntity(player);
    state.UpsertEntity(enemyA);
    state.UpsertEntity(enemyB);

    var profile = new SimAbilityProfile { Id = "spec.test.aoe" };
    profile.AbilitiesByFlag[InputActionFlags.Skill1] = new SimAbilityDefinition
    {
        Id = "ability.test.aoe",
        Slot = SimAbilitySlot.E,
        InputBehavior = SimAbilityInputBehavior.Tap,
        BaseCooldownTicks = 20,
        CooldownMinTicks = 10,
        RangeMilli = 2200,
        MaxTargets = 2,
        HasDamageEffect = false,
        Effects =
        {
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.DealDamage, CoefficientPermille = 1000, Flat = 4 },
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.ApplyStatus, StatusId = "status.test.mark" }
        }
    };
    state.RegisterAbilityProfile(profile);
    player.AbilityProfileId = profile.Id;

    player.ActionFlags = InputActionFlags.Skill1;
    OverworldSimulator.Step(state, rules);

    Assert(enemyA.Health < 160, "aoe ability did not damage nearest target");
    Assert(enemyB.Health < 160, "aoe ability did not damage second target");
    Assert(enemyA.Statuses.ContainsKey("status.test.mark"), "aoe ability did not apply status to first target");
    Assert(enemyB.Statuses.ContainsKey("status.test.mark"), "aoe ability did not apply status to second target");
}

void HitscanPrimitiveAssertions()
{
    var rules = OverworldSimRules.Default;
    var tune = CharacterStatTuning.Default;
    var state = new OverworldSimState { Seed = 84 };

    var player = new SimEntityState
    {
        EntityId = 1,
        Kind = EntityKind.Player,
        PositionXMilli = 0,
        PositionYMilli = 0
    };
    player.Character.Attributes = CharacterAttributes.Default;
    player.Character.RecalculateDerivedStats(tune);
    player.Health = player.Character.DerivedStats.MaxHealth;
    player.SpenderResource = player.Character.DerivedStats.MaxSpenderResource * 1000;

    var enemyA = new SimEntityState { EntityId = 2, Kind = EntityKind.Enemy, PositionXMilli = 1300, PositionYMilli = 0, Health = 200 };
    enemyA.Character.Attributes = CharacterAttributes.Default;
    enemyA.Character.RecalculateDerivedStats(tune);
    var enemyB = new SimEntityState { EntityId = 3, Kind = EntityKind.Enemy, PositionXMilli = 1700, PositionYMilli = 0, Health = 200 };
    enemyB.Character.Attributes = CharacterAttributes.Default;
    enemyB.Character.RecalculateDerivedStats(tune);

    state.UpsertEntity(player);
    state.UpsertEntity(enemyA);
    state.UpsertEntity(enemyB);

    var profile = new SimAbilityProfile { Id = "spec.test.hitscan" };
    profile.AbilitiesByFlag[InputActionFlags.Skill1] = new SimAbilityDefinition
    {
        Id = "ability.test.hitscan",
        Slot = SimAbilitySlot.E,
        InputBehavior = SimAbilityInputBehavior.Tap,
        BaseCooldownTicks = 20,
        CooldownMinTicks = 10,
        RangeMilli = 3000,
        MaxTargets = 2,
        HasDamageEffect = false,
        Effects =
        {
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.HitscanTrace, CoefficientPermille = 1400, Flat = 6 }
        }
    };
    state.RegisterAbilityProfile(profile);
    player.AbilityProfileId = profile.Id;
    player.ActionFlags = InputActionFlags.Skill1;

    OverworldSimulator.Step(state, rules);

    Assert(enemyA.Health < 200, "hitscan primitive did not damage first target");
    Assert(enemyB.Health < 200, "hitscan primitive did not damage second target");
}

void ThreatTargetingAssertions()
{
    var rules = OverworldSimRules.Default;
    var tune = CharacterStatTuning.Default;
    var state = new OverworldSimState { Seed = 77 };

    var tank = new SimEntityState { EntityId = 1, Kind = EntityKind.Player, PositionXMilli = -2600, PositionYMilli = 0 };
    tank.Character.Attributes = CharacterAttributes.Default;
    tank.Character.RecalculateDerivedStats(tune);
    tank.Health = tank.Character.DerivedStats.MaxHealth;

    var dps = new SimEntityState { EntityId = 2, Kind = EntityKind.Player, PositionXMilli = 800, PositionYMilli = 0 };
    dps.Character.Attributes = CharacterAttributes.Default;
    dps.Character.RecalculateDerivedStats(tune);
    dps.Health = dps.Character.DerivedStats.MaxHealth;

    var enemy = new SimEntityState { EntityId = 3, Kind = EntityKind.Enemy, PositionXMilli = 1200, PositionYMilli = 0, Health = 200 };
    enemy.Character.Attributes = CharacterAttributes.Default;
    enemy.Character.RecalculateDerivedStats(tune);

    state.UpsertEntity(tank);
    state.UpsertEntity(dps);
    state.UpsertEntity(enemy);

    OverworldSimulator.ApplyThreat(enemy, tank.EntityId, 5000);
    var xBefore = enemy.PositionXMilli;
    OverworldSimulator.Step(state, rules);

    Assert(enemy.PositionXMilli < xBefore, "enemy did not retarget movement toward highest-threat player");
    Assert(enemy.ThreatByPlayerEntityId.TryGetValue(tank.EntityId, out var threatValue) && threatValue > 0, "threat table did not retain applied threat");
}

void LinkPrimitiveAssertions()
{
    var rules = OverworldSimRules.Default;
    var tune = CharacterStatTuning.Default;
    var state = new OverworldSimState { Seed = 909 };

    var player = new SimEntityState
    {
        EntityId = 1,
        Kind = EntityKind.Player,
        PositionXMilli = 0,
        PositionYMilli = 0
    };
    player.Character.Attributes = CharacterAttributes.Default;
    player.Character.RecalculateDerivedStats(tune);
    player.Health = player.Character.DerivedStats.MaxHealth;
    player.SpenderResource = player.Character.DerivedStats.MaxSpenderResource * 1000;

    var enemy = new SimEntityState
    {
        EntityId = 2,
        Kind = EntityKind.Enemy,
        PositionXMilli = 2000,
        PositionYMilli = 0,
        Health = 220
    };
    enemy.Character.Attributes = CharacterAttributes.Default;
    enemy.Character.RecalculateDerivedStats(tune);

    state.UpsertEntity(player);
    state.UpsertEntity(enemy);

    var profile = new SimAbilityProfile { Id = "spec.test.links" };
    profile.AbilitiesByFlag[InputActionFlags.Skill2] = new SimAbilityDefinition
    {
        Id = "ability.test.link_create",
        Slot = SimAbilitySlot.R,
        InputBehavior = SimAbilityInputBehavior.Tap,
        BaseCooldownTicks = 10,
        CooldownMinTicks = 5,
        RangeMilli = 4000,
        MaxTargets = 1,
        HasDamageEffect = false,
        Effects =
        {
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.CreateLink, LinkDefId = "link.dreadweaver.menace.chain_snare" }
        }
    };
    profile.AbilitiesByFlag[InputActionFlags.Skill8] = new SimAbilityDefinition
    {
        Id = "ability.test.link_break",
        Slot = SimAbilitySlot.Skill4,
        InputBehavior = SimAbilityInputBehavior.Tap,
        BaseCooldownTicks = 10,
        CooldownMinTicks = 5,
        RangeMilli = 3000,
        MaxTargets = 1,
        HasDamageEffect = false,
        Effects =
        {
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.BreakLink }
        }
    };

    state.RegisterAbilityProfile(profile);
    player.AbilityProfileId = profile.Id;

    var distanceBefore = Math.Abs(enemy.PositionXMilli - player.PositionXMilli);
    var enemyHpBefore = enemy.Health;
    player.ActionFlags = InputActionFlags.Skill2;
    OverworldSimulator.Step(state, rules);
    Assert(state.Links.Count == 1, "create link primitive did not create a link");

    player.ActionFlags = InputActionFlags.None;
    for (var i = 0; i < 20; i++)
    {
        OverworldSimulator.Step(state, rules);
    }

    var distanceAfter = Math.Abs(enemy.PositionXMilli - player.PositionXMilli);
    Assert(distanceAfter < distanceBefore, "active link did not pull target toward owner");
    Assert(enemy.Health < enemyHpBefore, "active link did not apply periodic damage");

    player.ActionFlags = InputActionFlags.Skill8;
    OverworldSimulator.Step(state, rules);
    Assert(state.Links.Count == 0, "break link primitive did not clear owner links");
}

void HealingAssertions()
{
    var rules = OverworldSimRules.Default;
    var tune = CharacterStatTuning.Default;
    var state = new OverworldSimState { Seed = 111 };

    var player = new SimEntityState
    {
        EntityId = 1,
        Kind = EntityKind.Player,
        PositionXMilli = 0,
        PositionYMilli = 0
    };
    player.Character.Attributes = CharacterAttributes.Default;
    player.Character.RecalculateDerivedStats(tune);
    player.Health = player.Character.DerivedStats.MaxHealth - 40;
    player.SpenderResource = player.Character.DerivedStats.MaxSpenderResource * 1000;

    var enemy = new SimEntityState
    {
        EntityId = 2,
        Kind = EntityKind.Enemy,
        PositionXMilli = 1400,
        PositionYMilli = 0,
        Health = 180
    };
    enemy.Character.Attributes = CharacterAttributes.Default;
    enemy.Character.RecalculateDerivedStats(tune);

    state.UpsertEntity(player);
    state.UpsertEntity(enemy);

    var profile = new SimAbilityProfile { Id = "spec.test.healing" };
    profile.AbilitiesByFlag[InputActionFlags.Skill4] = new SimAbilityDefinition
    {
        Id = "ability.test.heal_self",
        Slot = SimAbilitySlot.T,
        InputBehavior = SimAbilityInputBehavior.Tap,
        BaseCooldownTicks = 30,
        CooldownMinTicks = 10,
        RangeMilli = 0,
        MaxTargets = 1,
        TargetTeam = SimTargetTeam.Self,
        HasDamageEffect = false,
        Effects =
        {
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.Heal, CoefficientPermille = 1000, Flat = 12 }
        }
    };
    profile.AbilitiesByFlag[InputActionFlags.Skill1] = new SimAbilityDefinition
    {
        Id = "ability.test.heal_zone",
        Slot = SimAbilitySlot.E,
        InputBehavior = SimAbilityInputBehavior.Tap,
        BaseCooldownTicks = 30,
        CooldownMinTicks = 10,
        RangeMilli = 3000,
        MaxTargets = 1,
        TargetTeam = SimTargetTeam.Ally,
        HasDamageEffect = false,
        Effects =
        {
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.SpawnZone, ZoneDefId = "zone.tidebinder.tidecaller.soothing_pool" }
        }
    };

    state.RegisterAbilityProfile(profile);
    player.AbilityProfileId = profile.Id;

    var hpBeforeSelfHeal = player.Health;
    player.ActionFlags = InputActionFlags.Skill4;
    OverworldSimulator.Step(state, rules);
    Assert(player.Health > hpBeforeSelfHeal, "heal primitive did not restore self health");

    player.Health = Math.Max(1, player.Health - 30);
    var hpBeforeZone = player.Health;
    player.ActionFlags = InputActionFlags.Skill1;
    OverworldSimulator.Step(state, rules);
    Assert(state.Zones.Count > 0, "healing zone cast did not spawn zone");

    player.ActionFlags = InputActionFlags.None;
    for (var i = 0; i < 40; i++)
    {
        OverworldSimulator.Step(state, rules);
    }

    Assert(player.Health > hpBeforeZone, "healing zone pulses did not restore player health");
}

void TempestDamageZoneAssertions()
{
    var rules = OverworldSimRules.Default;
    var tune = CharacterStatTuning.Default;
    var state = new OverworldSimState { Seed = 121 };

    var player = new SimEntityState
    {
        EntityId = 1,
        Kind = EntityKind.Player,
        PositionXMilli = 0,
        PositionYMilli = 0
    };
    player.Character.Attributes = CharacterAttributes.Default;
    player.Character.RecalculateDerivedStats(tune);
    player.Health = player.Character.DerivedStats.MaxHealth;
    player.SpenderResource = player.Character.DerivedStats.MaxSpenderResource * 1000;

    var enemy = new SimEntityState
    {
        EntityId = 2,
        Kind = EntityKind.Enemy,
        PositionXMilli = 1300,
        PositionYMilli = 0,
        Health = 220
    };
    enemy.Character.Attributes = CharacterAttributes.Default;
    enemy.Character.RecalculateDerivedStats(tune);

    state.UpsertEntity(player);
    state.UpsertEntity(enemy);

    var profile = new SimAbilityProfile { Id = "spec.test.tempest" };
    profile.AbilitiesByFlag[InputActionFlags.Skill1] = new SimAbilityDefinition
    {
        Id = "ability.test.tempest_zone",
        Slot = SimAbilitySlot.E,
        InputBehavior = SimAbilityInputBehavior.Tap,
        BaseCooldownTicks = 20,
        CooldownMinTicks = 10,
        RangeMilli = 3000,
        TargetTeam = SimTargetTeam.Enemy,
        MaxTargets = 1,
        HasDamageEffect = false,
        Effects =
        {
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.SpawnZone, ZoneDefId = "zone.tidebinder.tempest.vortex_pool" }
        }
    };

    state.RegisterAbilityProfile(profile);
    player.AbilityProfileId = profile.Id;

    var hpBefore = enemy.Health;
    player.ActionFlags = InputActionFlags.Skill1;
    OverworldSimulator.Step(state, rules);
    Assert(state.Zones.Count > 0, "tempest vortex cast did not spawn zone");

    player.ActionFlags = InputActionFlags.None;
    for (var i = 0; i < 40; i++)
    {
        OverworldSimulator.Step(state, rules);
    }

    Assert(enemy.Health < hpBefore, "tempest vortex zone did not damage enemy over time");
}

void TargetedCastValidationAssertions()
{
    var rules = OverworldSimRules.Default;
    var tune = CharacterStatTuning.Default;
    var state = new OverworldSimState { Seed = 343 };

    var player = new SimEntityState
    {
        EntityId = 1,
        Kind = EntityKind.Player,
        PositionXMilli = 0,
        PositionYMilli = 0
    };
    player.Character.Attributes = CharacterAttributes.Default;
    player.Character.RecalculateDerivedStats(tune);
    player.Health = player.Character.DerivedStats.MaxHealth;
    player.SpenderResource = player.Character.DerivedStats.MaxSpenderResource * 1000;

    var enemy = new SimEntityState
    {
        EntityId = 2,
        Kind = EntityKind.Enemy,
        PositionXMilli = 7_000,
        PositionYMilli = 0,
        Health = 260
    };
    enemy.Character.Attributes = CharacterAttributes.Default;
    enemy.Character.RecalculateDerivedStats(tune);

    state.UpsertEntity(player);
    state.UpsertEntity(enemy);

    var profile = new SimAbilityProfile { Id = "spec.test.cast_validation" };
    profile.AbilitiesByFlag[InputActionFlags.Skill7] = new SimAbilityDefinition
    {
        Id = "ability.test.whirlpool",
        Slot = SimAbilitySlot.Skill3,
        InputBehavior = SimAbilityInputBehavior.Tap,
        BaseCooldownTicks = 30,
        CooldownMinTicks = 10,
        RangeMilli = 3000,
        TargetTeam = SimTargetTeam.Enemy,
        MaxTargets = 1,
        HasDamageEffect = false,
        Effects =
        {
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.SpendResource, Amount = 24_000 },
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.ConsumeStatus, StatusId = "status.tidebinder.tempest.soaked", Amount = 3 },
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.DealDamage, CoefficientPermille = 1500, Flat = 6 }
        }
    };

    state.RegisterAbilityProfile(profile);
    player.AbilityProfileId = profile.Id;

    var spenderBeforeNoTarget = player.SpenderResource;
    player.ActionFlags = InputActionFlags.Skill7;
    OverworldSimulator.Step(state, rules);
    Assert(player.SpenderResource == spenderBeforeNoTarget, "targeted cast without target should not spend resource");
    Assert(player.SkillCooldownTicks[6] == 0, "targeted cast without target should not start cooldown");

    enemy.PositionXMilli = 1_200;
    enemy.PositionYMilli = 0;
    enemy.Statuses["status.tidebinder.tempest.soaked"] = new SimStatusState
    {
        Id = "status.tidebinder.tempest.soaked",
        Stacks = 3,
        RemainingTicks = 300
    };
    var hpBeforeCast = enemy.Health;
    var spenderBeforeCast = player.SpenderResource;
    player.ActionFlags = InputActionFlags.Skill7;
    OverworldSimulator.Step(state, rules);
    Assert(player.SpenderResource < spenderBeforeCast, "targeted cast with target should spend resource");
    Assert(player.SkillCooldownTicks[6] > 0, "targeted cast with target should start cooldown");
    Assert(enemy.Health < hpBeforeCast, "targeted cast with target should damage enemy");
    Assert(player.DebugLastConsumedStatusStacks > 0, "consume-status cast should report consumed stacks");
}

void InventoryGridAndEquipAssertions()
{
    var catalog = new DictionaryInventoryItemCatalog(new Dictionary<string, InventoryItemDefinition>(StringComparer.Ordinal)
    {
        ["item.sword.iron"] = new InventoryItemDefinition
        {
            ItemCode = "item.sword.iron",
            MaxStack = 1,
            GridWidth = 1,
            GridHeight = 3,
            EquipSlots = { EquipSlot.MainHand },
            EquipUniqueKey = "weapon.mainhand"
        },
        ["item.shield.wood"] = new InventoryItemDefinition
        {
            ItemCode = "item.shield.wood",
            MaxStack = 1,
            GridWidth = 2,
            GridHeight = 2,
            EquipSlots = { EquipSlot.OffHand },
            EquipUniqueKey = "weapon.offhand"
        },
        ["item.ring.guard"] = new InventoryItemDefinition
        {
            ItemCode = "item.ring.guard",
            MaxStack = 1,
            GridWidth = 1,
            GridHeight = 1,
            EquipSlots = { EquipSlot.Ring1, EquipSlot.Ring2 },
            EquipUniqueKey = "ring.guard"
        },
        ["item.potion.small"] = new InventoryItemDefinition
        {
            ItemCode = "item.potion.small",
            MaxStack = 20,
            GridWidth = 1,
            GridHeight = 1
        }
    });

    var inventory = new InventorySnapshot
    {
        BackpackWidth = 5,
        BackpackHeight = 6
    };
    InventoryRules.EnsureBackpackSize(inventory);

    var addSword = InventoryOps.AddToBackpack(inventory, catalog, new InventoryItemStack { ItemCode = "item.sword.iron", Quantity = 1 });
    var addShield = InventoryOps.AddToBackpack(inventory, catalog, new InventoryItemStack { ItemCode = "item.shield.wood", Quantity = 1 });
    var addRings = InventoryOps.AddToBackpack(inventory, catalog, new InventoryItemStack { ItemCode = "item.ring.guard", Quantity = 2 });
    var addPotions = InventoryOps.AddToBackpack(inventory, catalog, new InventoryItemStack { ItemCode = "item.potion.small", Quantity = 12 });
    Assert(addSword.Success && addShield.Success && addRings.Success && addPotions.Success, "inventory add failed");

    GridCoord FindCell(string itemCode)
    {
        for (var i = 0; i < inventory.BackpackItems.Count; i++)
        {
            if (string.Equals(inventory.BackpackItems[i].ItemCode, itemCode, StringComparison.Ordinal))
            {
                return inventory.BackpackItems[i].Position;
            }
        }

        return new GridCoord { X = -1, Y = -1 };
    }

    var swordCell = FindCell("item.sword.iron");
    var sword = inventory.BackpackItems.Find(x => string.Equals(x.ItemCode, "item.sword.iron", StringComparison.Ordinal));
    Assert(sword is not null, "sword should exist in backpack before move");
    var moveTarget = new GridCoord { X = -1, Y = -1 };
    for (var y = 0; y < inventory.BackpackHeight; y++)
    {
        for (var x = 0; x < inventory.BackpackWidth; x++)
        {
            var candidate = new GridCoord { X = x, Y = y };
            if (candidate.X == swordCell.X && candidate.Y == swordCell.Y)
            {
                continue;
            }

            if (InventoryRules.CanPlaceAt(inventory, catalog, "item.sword.iron", candidate, sword!.InstanceId))
            {
                moveTarget = candidate;
                break;
            }
        }

        if (moveTarget.X >= 0)
        {
            break;
        }
    }

    var move = InventoryOps.MoveBackpack(inventory, catalog, new InventoryMoveRequest
    {
        FromContainer = InventoryContainerKind.Backpack,
        ToContainer = InventoryContainerKind.Backpack,
        FromBackpackCell = swordCell,
        ToBackpackCell = moveTarget,
        Quantity = 5
    });
    Assert(move.Success, "grid move failed");

    var equipSword = InventoryOps.BackpackToEquip(inventory, catalog, new BackpackToEquipRequest
    {
        BackpackCell = FindCell("item.sword.iron"),
        TargetSlot = EquipSlot.MainHand
    });
    var shieldCell = FindCell("item.shield.wood");
    var shieldIndex = shieldCell.Y * inventory.BackpackWidth + shieldCell.X;
    var equipShield = InventoryOps.QuickEquip(inventory, catalog, backpackIndex: shieldIndex);
    Assert(equipSword.Success && equipShield.Success, "quick equip failed");
    Assert(inventory.Equipment.TryGetValue(EquipSlot.MainHand, out var mh) && mh is not null, "main hand not equipped");
    Assert(inventory.Equipment.TryGetValue(EquipSlot.OffHand, out var oh) && oh is not null, "off hand not equipped");

    var ringFromBackpack = InventoryOps.BackpackToEquip(inventory, catalog, new BackpackToEquipRequest
    {
        BackpackCell = FindCell("item.ring.guard"),
        TargetSlot = EquipSlot.Ring1
    });
    Assert(ringFromBackpack.Success, "ring equip 1 failed");

    var secondRingCell = FindCell("item.ring.guard");
    var secondRingFromBackpack = InventoryOps.BackpackToEquip(inventory, catalog, new BackpackToEquipRequest
    {
        BackpackCell = secondRingCell,
        TargetSlot = EquipSlot.Ring2
    });
    Assert(!secondRingFromBackpack.Success, "unique ring should not equip twice");

    var encode = InventoryJsonCodec.Serialize(inventory);
    var decode = InventoryJsonCodec.DeserializeOrDefault(encode, 5, 6);
    Assert(decode.BackpackWidth == 5 && decode.BackpackHeight == 6, "inventory json codec layout mismatch");
    Assert(decode.BackpackItems.Count > 0, "inventory json codec item decode mismatch");
}

ProtocolRoundtripAssertions();
RngAssertions();
CharacterMathAssertions();
CombatAssertions();
LootPickupAssertions();
DeterminismReplayAssertions();
TauntAndCcAssertions();
ZoneSpawnAssertions();
MultiTargetAbilityAssertions();
HitscanPrimitiveAssertions();
ThreatTargetingAssertions();
LinkPrimitiveAssertions();
HealingAssertions();
TempestDamageZoneAssertions();
TargetedCastValidationAssertions();
InventoryGridAndEquipAssertions();

if (failures.Count > 0)
{
    Console.Error.WriteLine("SharedSim tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($" - {failure}");
    }

    return 1;
}

Console.WriteLine("SharedSim tests passed.");
return 0;
