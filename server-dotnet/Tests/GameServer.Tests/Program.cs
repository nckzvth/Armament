using System.Net;
using System.Net.Sockets;
using System.Linq;
using Armament.GameServer.Campaign;
using Armament.GameServer;
using Armament.GameServer.Persistence;
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

await using var server = new AuthoritativeServer(port: 19100, simulationHz: 60, snapshotHz: 10);
server.Start();

using var clientA = new UdpClient(0);
using var clientB = new UdpClient(0);
using var clientC = new UdpClient(0);

clientA.Client.ReceiveTimeout = 3000;
clientB.Client.ReceiveTimeout = 3000;
clientC.Client.ReceiveTimeout = 3000;

var endpoint = new IPEndPoint(IPAddress.Loopback, 19100);

async Task<IProtocolMessage?> ExchangeAsync(UdpClient client, IProtocolMessage outgoing)
{
    var payload = ProtocolCodec.Encode(outgoing);
    await client.SendAsync(payload, payload.Length, endpoint);

    for (var i = 0; i < 8; i++)
    {
        var result = await ReceiveWithTimeoutAsync(client, TimeSpan.FromMilliseconds(500));
        if (result is null)
        {
            continue;
        }

        if (ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded))
        {
            return decoded;
        }
    }

    return null;
}

var helloA = await ExchangeAsync(clientA, new ClientHello { ClientNonce = 1 });
var helloB = await ExchangeAsync(clientB, new ClientHello { ClientNonce = 2 });
var helloC = await ExchangeAsync(clientC, new ClientHello { ClientNonce = 3 });
Assert(helloA is ServerHello, "client A did not receive server hello");
Assert(helloB is ServerHello, "client B did not receive server hello");
Assert(helloC is ServerHello, "client C did not receive server hello");

var joinA = await ExchangeAsync(clientA, new JoinOverworldRequest { CharacterName = "Alpha" });
var joinB = await ExchangeAsync(clientB, new JoinOverworldRequest { CharacterName = "Bravo" });
var joinC = await ExchangeAsync(clientC, new JoinOverworldRequest { CharacterName = "Charlie" });
Assert(joinA is JoinOverworldAccepted, "client A did not join overworld");
Assert(joinB is JoinOverworldAccepted, "client B did not join overworld");
Assert(joinC is JoinOverworldAccepted, "client C did not join overworld");
var localEntityId = (joinA as JoinOverworldAccepted)?.EntityId ?? 0;

var input = new InputCommand
{
    Sequence = 1,
    ClientTick = 1,
    MoveX = Quantization.QuantizeInput(1f),
    MoveY = 0,
    ActionFlags = InputActionFlags.FastAttackHold
};

var inputPayload = ProtocolCodec.Encode(input);
await clientA.SendAsync(inputPayload, inputPayload.Length, endpoint);

WorldSnapshot? snapshotA = null;
var deadline = DateTime.UtcNow.AddSeconds(3);
while (DateTime.UtcNow < deadline)
{
    var result = await ReceiveWithTimeoutAsync(clientA, TimeSpan.FromMilliseconds(500));
    if (result is null)
    {
        continue;
    }

    if (ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded) && decoded is WorldSnapshot snap)
    {
        snapshotA = snap;
        break;
    }
}

Assert(snapshotA is not null, "client A did not receive a world snapshot");
Assert(snapshotA is not null && snapshotA.Entities.Count >= 2, "snapshot missing remote entity");
Assert(snapshotA is not null && snapshotA.Entities.Exists(x => x.Kind == EntityKind.Enemy), "snapshot missing enemy archetype");
Assert(snapshotA is not null && snapshotA.ZoneKind == ZoneKind.Overworld, "initial snapshot was not overworld");

var minEnemyHealth = ushort.MaxValue;
var maxBuilder = (ushort)0;
var hasLoot = false;

for (var i = 0; i < 100; i++)
{
    var cmd = new InputCommand
    {
        Sequence = (uint)(2 + i),
        ClientTick = (uint)(2 + i),
        MoveX = Quantization.QuantizeInput(1f),
        MoveY = 0,
        ActionFlags = InputActionFlags.FastAttackHold | InputActionFlags.Pickup
    };

    var payload = ProtocolCodec.Encode(cmd);
    await clientA.SendAsync(payload, payload.Length, endpoint);

    var result = await ReceiveWithTimeoutAsync(clientA, TimeSpan.FromMilliseconds(120));
    if (result is null || !ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded) || decoded is not WorldSnapshot stepSnapshot)
    {
        continue;
    }

    foreach (var entity in stepSnapshot.Entities)
    {
        if (entity.Kind == EntityKind.Enemy)
        {
            if (entity.Health < minEnemyHealth)
            {
                minEnemyHealth = entity.Health;
            }
        }

        if (entity.EntityId == localEntityId && entity.BuilderResource > maxBuilder)
        {
            maxBuilder = entity.BuilderResource;
        }

        if (entity.Kind == EntityKind.Loot)
        {
            hasLoot = true;
        }

    }
}

Assert(maxBuilder > 0, "combat loop did not build player builder resource");
Assert(minEnemyHealth < 120 || hasLoot, "combat loop did not affect enemy health/loot state");

// Dungeon join should be rejected when not near the portal.
var earlyDungeonReqPayload = ProtocolCodec.Encode(new JoinDungeonRequest());
await clientB.SendAsync(earlyDungeonReqPayload, earlyDungeonReqPayload.Length, endpoint);
var earlyJoinAccepted = false;
var earlyJoinDeadline = DateTime.UtcNow.AddMilliseconds(900);
while (DateTime.UtcNow < earlyJoinDeadline)
{
    var result = await ReceiveWithTimeoutAsync(clientB, TimeSpan.FromMilliseconds(120));
    if (result is null || !ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded))
    {
        continue;
    }

    if (decoded is JoinDungeonAccepted)
    {
        earlyJoinAccepted = true;
        break;
    }
}

Assert(!earlyJoinAccepted, "dungeon join should not be accepted when far from portal");

// Client C spawns at x=4000 (3rd player), which is inside portal interaction range.
var dungeonReqPayload = ProtocolCodec.Encode(new JoinDungeonRequest());
await clientC.SendAsync(dungeonReqPayload, dungeonReqPayload.Length, endpoint);

JoinDungeonAccepted? dungeonAccepted = null;
var acceptedDeadline = DateTime.UtcNow.AddSeconds(4);
while (DateTime.UtcNow < acceptedDeadline)
{
    var result = await ReceiveWithTimeoutAsync(clientC, TimeSpan.FromMilliseconds(500));
    if (result is null || !ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded))
    {
        continue;
    }

    if (decoded is JoinDungeonAccepted accepted)
    {
        dungeonAccepted = accepted;
        break;
    }
}

Assert(dungeonAccepted is not null, "dungeon join request was not accepted");

WorldSnapshot? dungeonSnapshot = null;
var dungeonDeadline = DateTime.UtcNow.AddSeconds(4);
while (DateTime.UtcNow < dungeonDeadline)
{
    var result = await ReceiveWithTimeoutAsync(clientC, TimeSpan.FromMilliseconds(500));
    if (result is null || !ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded) || decoded is not WorldSnapshot snap)
    {
        continue;
    }

    if (snap.ZoneKind == ZoneKind.Dungeon)
    {
        dungeonSnapshot = snap;
        break;
    }
}

Assert(dungeonSnapshot is not null, "did not receive dungeon snapshot after join");
Assert(dungeonSnapshot is not null && dungeonSnapshot.Entities.Exists(x => x.Kind == EntityKind.Enemy), "dungeon snapshot missing tougher encounter enemy");

// Regression: non-LMB skill usage in dungeon must not stall snapshot stream.
var dungeonTickBeforeSkills = dungeonSnapshot?.ServerTick ?? 0;
for (var i = 0; i < 16; i++)
{
    var action = (i % 4) switch
    {
        0 => InputActionFlags.Skill1,
        1 => InputActionFlags.Skill2,
        2 => InputActionFlags.Skill5,
        _ => InputActionFlags.Skill8
    };

    var skillCmd = new InputCommand
    {
        Sequence = (uint)(400 + i),
        ClientTick = (uint)(400 + i),
        MoveX = 0,
        MoveY = 0,
        ActionFlags = action
    };

    var payload = ProtocolCodec.Encode(skillCmd);
    await clientC.SendAsync(payload, payload.Length, endpoint);
}

WorldSnapshot? postSkillDungeonSnapshot = null;
var postSkillDeadline = DateTime.UtcNow.AddSeconds(3);
while (DateTime.UtcNow < postSkillDeadline)
{
    var result = await ReceiveWithTimeoutAsync(clientC, TimeSpan.FromMilliseconds(250));
    if (result is null || !ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded) || decoded is not WorldSnapshot snap)
    {
        continue;
    }

    if (snap.ZoneKind == ZoneKind.Dungeon && snap.ServerTick > dungeonTickBeforeSkills)
    {
        postSkillDungeonSnapshot = snap;
        break;
    }
}

Assert(postSkillDungeonSnapshot is not null, "dungeon snapshots stalled after non-LMB skill inputs");

// Explicit disconnect should evict stale entities from the world.
var disconnectPayload = ProtocolCodec.Encode(new DisconnectRequest());
await clientB.SendAsync(disconnectPayload, disconnectPayload.Length, endpoint);
var disconnectDeadline = DateTime.UtcNow.AddSeconds(2);
var disconnectedEntityGone = false;
while (DateTime.UtcNow < disconnectDeadline)
{
    var result = await ReceiveWithTimeoutAsync(clientA, TimeSpan.FromMilliseconds(250));
    if (result is null || !ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded) || decoded is not WorldSnapshot snap)
    {
        continue;
    }

    var playerCount = snap.Entities.Count(x => x.Kind == EntityKind.Player);
    if (playerCount <= 1)
    {
        disconnectedEntityGone = true;
        break;
    }
}

Assert(disconnectedEntityGone, "disconnect request did not remove disconnected player entity from snapshots");

await QueueBackpressureAssertionsAsync();
await QueueRetryAssertionsAsync();
await ProfileLoadAssertionsAsync();
await ProfileSaveEnqueueAssertionsAsync();
await ProfileSpecRepairAssertionsAsync();
await ProfileSpecRequestedOverrideAssertionsAsync();
await LinkReplicationAssertionsAsync();
CampPerimeterRuntimeAssertions();

if (failures.Count > 0)
{
    Console.Error.WriteLine("GameServer tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($" - {failure}");
    }

    return 1;
}

Console.WriteLine("GameServer tests passed.");
return 0;

void CampPerimeterRuntimeAssertions()
{
    var runtime = new CampPerimeterRuntime(new Dictionary<string, CampaignEncounterDefinition>(StringComparer.Ordinal)
    {
        ["enc.cp.perimeter_breach"] = new CampaignEncounterDefinition
        {
            Id = "enc.cp.perimeter_breach",
            CompletionEventKind = CampaignCompletionEventKind.EnemyKilled,
            CompletionCount = 2,
            RewardItemCode = "item.quest.camp_supply",
            RewardItemQuantity = 1
        }
    }, new Dictionary<string, CampaignQuestDefinition>(StringComparer.Ordinal)
    {
        ["quest.act1.contract_board"] = new CampaignQuestDefinition
        {
            Id = "quest.act1.contract_board",
            Objectives =
            {
                new CampaignQuestObjectiveDefinition
                {
                    Type = "TalkToNpc",
                    TargetId = "npc.quartermaster",
                    Count = 1
                },
                new CampaignQuestObjectiveDefinition
                {
                    Type = "CompleteEncounter",
                    TargetId = "enc.cp.perimeter_breach",
                    Count = 1
                }
            }
        }
    });

    var playerEntityId = 42u;
    var npcRuntimeId = 9001u;
    var characterId = Guid.NewGuid();
    runtime.RestoreCharacterState(characterId, "{}");
    var objectiveSnapshots = runtime.BuildObjectiveSnapshots(characterId);
    Assert(objectiveSnapshots.Count > 0, "camp perimeter runtime did not build quest objective snapshots for character");
    Assert(objectiveSnapshots.Any(x => x.Kind == "TalkToNpc" && x.Current == 0), "talk objective should not be pre-completed");

    var completions = runtime.Consume(
        new List<SimEventRecord>
        {
            new() { Kind = SimEventKind.NpcInteracted, PlayerEntityId = playerEntityId, SubjectEntityId = npcRuntimeId },
            new() { Kind = SimEventKind.EnemyKilled, PlayerEntityId = playerEntityId },
            new() { Kind = SimEventKind.EnemyKilled, PlayerEntityId = playerEntityId }
        },
        entity => entity == playerEntityId ? characterId : null,
        resolveNpcIdByRuntimeId: runtimeNpcId => runtimeNpcId == npcRuntimeId ? "npc.quartermaster" : null);

    Assert(completions.Count == 1, "camp perimeter runtime did not emit completion");
    Assert(completions.Count == 1 && completions[0].CharacterId == characterId, "camp perimeter runtime mapped wrong character");
    Assert(completions.Count == 1 && completions[0].RewardItemCode == "item.quest.camp_supply", "camp perimeter runtime reward mismatch");

    var secondPass = runtime.Consume(
        new List<SimEventRecord> { new() { Kind = SimEventKind.EnemyKilled, PlayerEntityId = playerEntityId } },
        entity => entity == playerEntityId ? characterId : null);
    Assert(secondPass.Count == 0, "camp perimeter runtime should not repeat completed encounter reward");
}

static async Task<UdpReceiveResult?> ReceiveWithTimeoutAsync(UdpClient client, TimeSpan timeout)
{
    using var cts = new CancellationTokenSource(timeout);
    try
    {
        return await client.ReceiveAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        return null;
    }
}

async Task QueueBackpressureAssertionsAsync()
{
    var handled = 0;
    await using var queue = new BoundedLootPersistenceQueue(
        capacity: 1,
        handler: async (_, token) =>
        {
            Interlocked.Increment(ref handled);
            await Task.Delay(120, token);
        });

    var req = new LootPersistenceRequest(
        CharacterId: Guid.NewGuid(),
        CharacterName: "QueueBackpressure",
        ClientId: 7,
        PlayerEntityId: 77,
        LootId: 1001,
        CurrencyDelta: 5,
        AutoLoot: true,
        ServerTick: 1,
        InstanceId: 0,
        ZoneName: "Overworld");

    var accepted = 0;
    for (var i = 0; i < 12; i++)
    {
        if (queue.TryEnqueue(req with { LootId = (uint)(1001 + i) }))
        {
            accepted++;
        }
    }

    await Task.Delay(500);

    Assert(queue.DroppedCount > 0, "bounded persistence queue did not apply backpressure");
    Assert(accepted > 0, "bounded persistence queue accepted no writes");
    Assert(handled > 0, "bounded persistence queue did not process accepted writes");
}

async Task LinkReplicationAssertionsAsync()
{
    var profile = new SimAbilityProfile { Id = "spec.test.links" };
    profile.AbilitiesByFlag[InputActionFlags.Skill2] = new SimAbilityDefinition
    {
        Id = "ability.test.link_r",
        Slot = SimAbilitySlot.R,
        InputBehavior = SimAbilityInputBehavior.Tap,
        BaseCooldownTicks = 30,
        CooldownMinTicks = 10,
        RangeMilli = 6000,
        MaxTargets = 1,
        HasDamageEffect = false,
        Effects =
        {
            new SimAbilityEffect { Primitive = SimAbilityPrimitive.CreateLink, LinkDefId = "link.dreadweaver.menace.chain_snare" }
        }
    };

    var loadedProfiles = new LoadedAbilityProfiles(
        new Dictionary<string, SimAbilityProfile>(StringComparer.Ordinal)
        {
            [SimAbilityProfiles.BuiltinV1.Id] = SimAbilityProfiles.BuiltinV1,
            [profile.Id] = profile
        },
        new Dictionary<string, SimZoneDefinition>(StringComparer.Ordinal),
        new Dictionary<string, SimLinkDefinition>(StringComparer.Ordinal),
        profile.Id,
        "test links");

    await using var server = new AuthoritativeServer(port: 19103, simulationHz: 60, snapshotHz: 10, abilityProfiles: loadedProfiles);
    server.Start();

    using var client = new UdpClient(0);
    client.Client.ReceiveTimeout = 3000;
    var endpoint = new IPEndPoint(IPAddress.Loopback, 19103);

    var helloPayload = ProtocolCodec.Encode(new ClientHello { ClientNonce = 11 });
    await client.SendAsync(helloPayload, helloPayload.Length, endpoint);

    var gotHello = false;
    var helloDeadline = DateTime.UtcNow.AddSeconds(2);
    while (DateTime.UtcNow < helloDeadline)
    {
        var result = await ReceiveWithTimeoutAsync(client, TimeSpan.FromMilliseconds(250));
        if (result is null || !ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded))
        {
            continue;
        }

        if (decoded is ServerHello)
        {
            gotHello = true;
            break;
        }
    }

    Assert(gotHello, "link replication test did not receive server hello");
    if (!gotHello)
    {
        return;
    }

    var joinPayload = ProtocolCodec.Encode(new JoinOverworldRequest
    {
        CharacterName = "LinkTest",
        BaseClassId = "dreadweaver",
        SpecId = profile.Id
    });
    await client.SendAsync(joinPayload, joinPayload.Length, endpoint);

    var joined = false;
    var joinDeadline = DateTime.UtcNow.AddSeconds(2);
    while (DateTime.UtcNow < joinDeadline)
    {
        var result = await ReceiveWithTimeoutAsync(client, TimeSpan.FromMilliseconds(250));
        if (result is null || !ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded))
        {
            continue;
        }

        if (decoded is JoinOverworldAccepted)
        {
            joined = true;
            break;
        }
    }

    Assert(joined, "link replication test could not join overworld");
    if (!joined)
    {
        return;
    }

    var cmd = new InputCommand
    {
        Sequence = 1,
        ClientTick = 1,
        MoveX = 0,
        MoveY = 0,
        ActionFlags = InputActionFlags.Skill2 // R slot for menace chain snare
    };

    var cmdPayload = ProtocolCodec.Encode(cmd);
    await client.SendAsync(cmdPayload, cmdPayload.Length, endpoint);

    var sawLink = false;
    var snapshotDeadline = DateTime.UtcNow.AddSeconds(3);
    while (DateTime.UtcNow < snapshotDeadline)
    {
        var result = await ReceiveWithTimeoutAsync(client, TimeSpan.FromMilliseconds(250));
        if (result is null || !ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded) || decoded is not WorldSnapshot snap)
        {
            continue;
        }

        if (snap.Links.Count > 0)
        {
            sawLink = true;
            break;
        }
    }

    Assert(sawLink, "menace R cast did not replicate a link entity");
}

async Task QueueRetryAssertionsAsync()
{
    var attempts = 0;
    await using var queue = new BoundedLootPersistenceQueue(
        capacity: 8,
        handler: (_, _) =>
        {
            var value = Interlocked.Increment(ref attempts);
            if (value <= 2)
            {
                throw new InvalidOperationException("transient failure");
            }

            return Task.CompletedTask;
        },
        maxAttempts: 3,
        retryDelay: TimeSpan.FromMilliseconds(10));

    var req = new LootPersistenceRequest(
        CharacterId: Guid.NewGuid(),
        CharacterName: "QueueRetry",
        ClientId: 8,
        PlayerEntityId: 88,
        LootId: 2001,
        CurrencyDelta: 5,
        AutoLoot: false,
        ServerTick: 2,
        InstanceId: 1,
        ZoneName: "Dungeon");

    _ = queue.TryEnqueue(req);
    await Task.Delay(200);

    Assert(queue.ProcessedCount == 1, "persistence queue did not recover after retries");
    Assert(queue.FailedCount >= 2, "persistence queue retry failures were not recorded");
}

async Task ProfileLoadAssertionsAsync()
{
    var profileService = new TestProfileService(new CharacterProfileData(
        Level: 4,
        Experience: 120,
        Currency: 99,
        Attributes: new CharacterAttributes { Might = 12, Will = 8, Alacrity = 11, Constitution = 10 },
        BaseClassId: "bastion",
        SpecId: "spec.bastion.bulwark",
        InventoryJson: "{}"));

    await using var profileServer = new AuthoritativeServer(
        port: 19101,
        simulationHz: 60,
        snapshotHz: 10,
        lootPersistenceSink: null,
        characterProfileService: profileService);
    profileServer.Start();

    using var client = new UdpClient(0);
    client.Client.ReceiveTimeout = 3000;
    var endpoint = new IPEndPoint(IPAddress.Loopback, 19101);

    _ = await ExchangeForEndpointAsync(client, endpoint, new ClientHello { ClientNonce = 9 });
    var join = await ExchangeForEndpointAsync(client, endpoint, new JoinOverworldRequest { CharacterName = "Profiled" });
    Assert(join is JoinOverworldAccepted, "profile-load server did not accept join");
    var localEntityId = (join as JoinOverworldAccepted)?.EntityId ?? 0;

    WorldSnapshot? snapshot = null;
    var deadline = DateTime.UtcNow.AddSeconds(3);
    while (DateTime.UtcNow < deadline)
    {
        var result = await ReceiveWithTimeoutAsync(client, TimeSpan.FromMilliseconds(300));
        if (result is null || !ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded) || decoded is not WorldSnapshot world)
        {
            continue;
        }

        snapshot = world;
        break;
    }

    Assert(snapshot is not null, "profile-load server did not produce snapshot");
    var entity = snapshot?.Entities.SingleOrDefault(x => x.EntityId == localEntityId);
    Assert(entity is not null, "profile-loaded local entity missing from snapshot");
    Assert(entity is not null && entity.Currency == 99, "profile-loaded currency did not apply");
}

async Task ProfileSaveEnqueueAssertionsAsync()
{
    var trackingService = new TrackingProfileService();

    await using var profileServer = new AuthoritativeServer(
        port: 19102,
        simulationHz: 60,
        snapshotHz: 10,
        lootPersistenceSink: null,
        characterProfileService: trackingService);
    profileServer.Start();

    using var clientA = new UdpClient(0);
    using var clientB = new UdpClient(0);
    using var clientC = new UdpClient(0);
    clientA.Client.ReceiveTimeout = 3000;
    clientB.Client.ReceiveTimeout = 3000;
    clientC.Client.ReceiveTimeout = 3000;
    var endpoint = new IPEndPoint(IPAddress.Loopback, 19102);

    _ = await ExchangeForEndpointAsync(clientA, endpoint, new ClientHello { ClientNonce = 10 });
    _ = await ExchangeForEndpointAsync(clientA, endpoint, new JoinOverworldRequest { CharacterName = "SaveA" });
    _ = await ExchangeForEndpointAsync(clientB, endpoint, new ClientHello { ClientNonce = 11 });
    _ = await ExchangeForEndpointAsync(clientB, endpoint, new JoinOverworldRequest { CharacterName = "SaveB" });
    _ = await ExchangeForEndpointAsync(clientC, endpoint, new ClientHello { ClientNonce = 12 });
    _ = await ExchangeForEndpointAsync(clientC, endpoint, new JoinOverworldRequest { CharacterName = "SaveC" });

    // Third joined player spawns at x=4000 and is already within portal interaction range.
    var joinDungeon = ProtocolCodec.Encode(new JoinDungeonRequest());
    await clientC.SendAsync(joinDungeon, joinDungeon.Length, endpoint);

    var deadline = DateTime.UtcNow.AddSeconds(3);
    while (DateTime.UtcNow < deadline)
    {
        var result = await ReceiveWithTimeoutAsync(clientC, TimeSpan.FromMilliseconds(250));
        if (result is null || !ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded))
        {
            continue;
        }

        if (decoded is JoinDungeonAccepted)
        {
            break;
        }
    }

    await Task.Delay(250);
    Assert(trackingService.SaveRequests.Count > 0, "profile save was not enqueued on overworld->dungeon transfer");
}

async Task ProfileSpecRepairAssertionsAsync()
{
    var staleProfileService = new RepairTrackingProfileService(
        new CharacterProfileData(
            Level: 7,
            Experience: 900,
            Currency: 33,
            Attributes: new CharacterAttributes { Might = 11, Will = 10, Alacrity = 9, Constitution = 12 },
            BaseClassId: "arbiter",
            SpecId: "spec.arbiter.aegis",
            InventoryJson: "{}"));

    var menaceProfile = new SimAbilityProfile { Id = "spec.dreadweaver.menace" };
    menaceProfile.AbilitiesByFlag[InputActionFlags.Skill2] = new SimAbilityDefinition
    {
        Id = "ability.test.menace_r",
        Slot = SimAbilitySlot.R,
        InputBehavior = SimAbilityInputBehavior.Tap,
        BaseCooldownTicks = 30,
        CooldownMinTicks = 10,
        RangeMilli = 6000,
        MaxTargets = 1,
        HasDamageEffect = false,
        Effects = { new SimAbilityEffect { Primitive = SimAbilityPrimitive.CreateLink, LinkDefId = "link.dreadweaver.menace.chain_snare" } }
    };

    var profiles = new LoadedAbilityProfiles(
        new Dictionary<string, SimAbilityProfile>(StringComparer.Ordinal)
        {
            [SimAbilityProfiles.BuiltinV1.Id] = SimAbilityProfiles.BuiltinV1,
            [menaceProfile.Id] = menaceProfile
        },
        new Dictionary<string, SimZoneDefinition>(StringComparer.Ordinal),
        new Dictionary<string, SimLinkDefinition>(StringComparer.Ordinal),
        fallbackSpecId: SimAbilityProfiles.BuiltinV1.Id,
        message: "repair-spec-test");

    await using var profileServer = new AuthoritativeServer(
        port: 19104,
        simulationHz: 60,
        snapshotHz: 10,
        lootPersistenceSink: null,
        characterProfileService: staleProfileService,
        abilityProfiles: profiles);
    profileServer.Start();

    using var client = new UdpClient(0);
    client.Client.ReceiveTimeout = 3000;
    var endpoint = new IPEndPoint(IPAddress.Loopback, 19104);

    _ = await ExchangeForEndpointAsync(client, endpoint, new ClientHello { ClientNonce = 13 });
    var join = await ExchangeForEndpointAsync(client, endpoint, new JoinOverworldRequest
    {
        CharacterName = "RepairCase",
        BaseClassId = "dreadweaver",
        SpecId = "spec.dreadweaver.menace"
    });

    Assert(join is JoinOverworldAccepted, "repair-spec server did not accept join");
    if (join is JoinOverworldAccepted accepted)
    {
        Assert(
            string.Equals(accepted.SpecId, "spec.dreadweaver.menace", StringComparison.Ordinal),
            $"join accepted wrong spec after repair path: '{accepted.SpecId}'");
        Assert(
            string.Equals(accepted.BaseClassId, "dreadweaver", StringComparison.Ordinal),
            $"join accepted wrong class after repair path: '{accepted.BaseClassId}'");
    }

    var deadline = DateTime.UtcNow.AddSeconds(2);
    while (DateTime.UtcNow < deadline && staleProfileService.SaveRequests.Count == 0)
    {
        await Task.Delay(20);
    }

    Assert(staleProfileService.SaveRequests.Count > 0, "repair path did not enqueue corrected profile save");
    if (staleProfileService.SaveRequests.Count > 0)
    {
        var saved = staleProfileService.SaveRequests[^1].Profile;
        Assert(
            string.Equals(saved.SpecId, "spec.dreadweaver.menace", StringComparison.Ordinal),
            $"repair save wrote wrong spec: '{saved.SpecId}'");
        Assert(
            string.Equals(saved.BaseClassId, "dreadweaver", StringComparison.Ordinal),
            $"repair save wrote wrong class: '{saved.BaseClassId}'");
    }
}

async Task ProfileSpecRequestedOverrideAssertionsAsync()
{
    var existingProfileService = new RepairTrackingProfileService(
        new CharacterProfileData(
            Level: 12,
            Experience: 1900,
            Currency: 42,
            Attributes: new CharacterAttributes { Might = 10, Will = 13, Alacrity = 11, Constitution = 12 },
            BaseClassId: "tidebinder",
            SpecId: "spec.tidebinder.tidecaller",
            InventoryJson: "{}"));

    var tidecallerProfile = new SimAbilityProfile { Id = "spec.tidebinder.tidecaller" };
    var tempestProfile = new SimAbilityProfile { Id = "spec.tidebinder.tempest" };

    var profiles = new LoadedAbilityProfiles(
        new Dictionary<string, SimAbilityProfile>(StringComparer.Ordinal)
        {
            [SimAbilityProfiles.BuiltinV1.Id] = SimAbilityProfiles.BuiltinV1,
            [tidecallerProfile.Id] = tidecallerProfile,
            [tempestProfile.Id] = tempestProfile
        },
        new Dictionary<string, SimZoneDefinition>(StringComparer.Ordinal),
        new Dictionary<string, SimLinkDefinition>(StringComparer.Ordinal),
        fallbackSpecId: SimAbilityProfiles.BuiltinV1.Id,
        message: "requested-spec-override-test");

    await using var profileServer = new AuthoritativeServer(
        port: 19105,
        simulationHz: 60,
        snapshotHz: 10,
        lootPersistenceSink: null,
        characterProfileService: existingProfileService,
        abilityProfiles: profiles);
    profileServer.Start();

    using var client = new UdpClient(0);
    client.Client.ReceiveTimeout = 3000;
    var endpoint = new IPEndPoint(IPAddress.Loopback, 19105);

    _ = await ExchangeForEndpointAsync(client, endpoint, new ClientHello { ClientNonce = 14 });
    var join = await ExchangeForEndpointAsync(client, endpoint, new JoinOverworldRequest
    {
        CharacterName = "RequestedOverride",
        BaseClassId = "tidebinder",
        SpecId = "spec.tidebinder.tempest"
    });

    Assert(join is JoinOverworldAccepted, "requested-spec-override server did not accept join");
    if (join is JoinOverworldAccepted accepted)
    {
        Assert(
            string.Equals(accepted.SpecId, "spec.tidebinder.tempest", StringComparison.Ordinal),
            $"join accepted wrong spec when requested should override persisted: '{accepted.SpecId}'");
        Assert(
            string.Equals(accepted.BaseClassId, "tidebinder", StringComparison.Ordinal),
            $"join accepted wrong class when requested should override persisted: '{accepted.BaseClassId}'");
    }

    var deadline = DateTime.UtcNow.AddSeconds(2);
    while (DateTime.UtcNow < deadline && existingProfileService.SaveRequests.Count == 0)
    {
        await Task.Delay(20);
    }

    Assert(existingProfileService.SaveRequests.Count > 0, "requested-spec-override path did not enqueue corrected profile save");
    if (existingProfileService.SaveRequests.Count > 0)
    {
        var saved = existingProfileService.SaveRequests[^1].Profile;
        Assert(
            string.Equals(saved.SpecId, "spec.tidebinder.tempest", StringComparison.Ordinal),
            $"requested-spec-override save wrote wrong spec: '{saved.SpecId}'");
        Assert(
            string.Equals(saved.BaseClassId, "tidebinder", StringComparison.Ordinal),
            $"requested-spec-override save wrote wrong class: '{saved.BaseClassId}'");
    }
}

async Task<IProtocolMessage?> ExchangeForEndpointAsync(UdpClient client, IPEndPoint endpoint, IProtocolMessage outgoing)
{
    var payload = ProtocolCodec.Encode(outgoing);
    await client.SendAsync(payload, payload.Length, endpoint);

    for (var i = 0; i < 10; i++)
    {
        var result = await ReceiveWithTimeoutAsync(client, TimeSpan.FromMilliseconds(300));
        if (result is null)
        {
            continue;
        }

        if (ProtocolCodec.TryDecode(result.Value.Buffer, out var decoded) && decoded is not null)
        {
            return decoded;
        }
    }

    return null;
}

sealed class TestProfileService(CharacterProfileData profile) : ICharacterProfileService
{
    private readonly Queue<CharacterProfileLoadResult> _results = new();

    public bool TryEnqueueLoad(CharacterProfileLoadRequest request)
    {
        var idBytes = new byte[16];
        BitConverter.GetBytes(request.ClientId).CopyTo(idBytes, 0);
        _results.Enqueue(new CharacterProfileLoadResult(
            request.EndpointKey,
            new Guid(idBytes),
            request.PreferredCharacterName,
            profile));
        return true;
    }

    public bool TryEnqueueSave(CharacterProfileSaveRequest request) => true;

    public bool TryDequeueLoaded(out CharacterProfileLoadResult result)
    {
        if (_results.Count == 0)
        {
            result = default!;
            return false;
        }

        result = _results.Dequeue();
        return true;
    }
}

sealed class TrackingProfileService : ICharacterProfileService
{
    private readonly Queue<CharacterProfileLoadResult> _results = new();
    public List<CharacterProfileSaveRequest> SaveRequests { get; } = new();

    public bool TryEnqueueLoad(CharacterProfileLoadRequest request)
    {
        var idBytes = new byte[16];
        BitConverter.GetBytes(request.ClientId).CopyTo(idBytes, 0);
        _results.Enqueue(new CharacterProfileLoadResult(
            request.EndpointKey,
            new Guid(idBytes),
            request.PreferredCharacterName,
            new CharacterProfileData(1, 0, 0, CharacterAttributes.Default, "bastion", "spec.bastion.bulwark", "{}")));
        return true;
    }

    public bool TryEnqueueSave(CharacterProfileSaveRequest request)
    {
        SaveRequests.Add(request);
        return true;
    }

    public bool TryDequeueLoaded(out CharacterProfileLoadResult result)
    {
        if (_results.Count == 0)
        {
            result = default!;
            return false;
        }

        result = _results.Dequeue();
        return true;
    }
}

sealed class RepairTrackingProfileService(CharacterProfileData profile) : ICharacterProfileService
{
    private readonly Queue<CharacterProfileLoadResult> _results = new();
    public List<CharacterProfileSaveRequest> SaveRequests { get; } = new();

    public bool TryEnqueueLoad(CharacterProfileLoadRequest request)
    {
        var idBytes = new byte[16];
        BitConverter.GetBytes(request.ClientId).CopyTo(idBytes, 0);
        _results.Enqueue(new CharacterProfileLoadResult(
            request.EndpointKey,
            new Guid(idBytes),
            request.PreferredCharacterName,
            profile));
        return true;
    }

    public bool TryEnqueueSave(CharacterProfileSaveRequest request)
    {
        SaveRequests.Add(request);
        return true;
    }

    public bool TryDequeueLoaded(out CharacterProfileLoadResult result)
    {
        if (_results.Count == 0)
        {
            result = default!;
            return false;
        }

        result = _results.Dequeue();
        return true;
    }
}
