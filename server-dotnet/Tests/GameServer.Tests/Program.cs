using System.Net;
using System.Net.Sockets;
using System.Linq;
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
        Attributes: new CharacterAttributes { Might = 12, Will = 8, Alacrity = 11, Constitution = 10 }));

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
            new CharacterProfileData(1, 0, 0, CharacterAttributes.Default)));
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
