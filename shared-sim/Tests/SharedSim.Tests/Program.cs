using Armament.SharedSim.Determinism;
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
                Currency = 0
            }
        }
    };

    var encoded = ProtocolCodec.Encode(snapshot);
    Assert(ProtocolCodec.TryDecode(encoded, out var decodedMsg), "snapshot decode failed");
    var decoded = decodedMsg as WorldSnapshot;
    Assert(decoded is not null, "decoded message type mismatch");
    Assert(decoded!.Entities.Count == 2, "snapshot entity count mismatch");
    Assert(decoded.LastProcessedInputSequence == 19, "snapshot ack mismatch");
    Assert(decoded.Entities[1].Kind == EntityKind.Enemy, "snapshot entity kind mismatch");

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

ProtocolRoundtripAssertions();
RngAssertions();
CharacterMathAssertions();
CombatAssertions();
LootPickupAssertions();
DeterminismReplayAssertions();

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
