using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Armament.GameServer.Persistence;
using Armament.Net;
using Armament.SharedSim.Protocol;

namespace Armament.GameServer;

public sealed class AuthoritativeServer : IAsyncDisposable
{
    private readonly UdpServerTransport _transport;
    private readonly Dictionary<string, ConnectionState> _connectionsByEndpoint = new();
    private readonly OverworldZone _overworldZone;
    private readonly Dictionary<uint, DungeonInstance> _dungeonInstances = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ILootPersistenceSink? _lootPersistenceSink;
    private readonly ICharacterProfileService? _characterProfileService;
    private readonly Dictionary<string, PendingJoinRequest> _pendingJoinRequests = new();
    private readonly uint _connectionTimeoutTicks;

    private Task? _loopTask;
    private uint _nextClientId = 1;
    private uint _nextDungeonInstanceId = 1;
    private uint _tick;

    public AuthoritativeServer(
        int port,
        int simulationHz = 60,
        int snapshotHz = 10,
        ILootPersistenceSink? lootPersistenceSink = null,
        ICharacterProfileService? characterProfileService = null)
    {
        if (simulationHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simulationHz));
        }

        if (snapshotHz <= 0 || snapshotHz > simulationHz)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotHz));
        }

        _transport = new UdpServerTransport(port);
        _overworldZone = new OverworldZone(simulationHz);
        _lootPersistenceSink = lootPersistenceSink;
        _characterProfileService = characterProfileService;
        SimulationHz = simulationHz;
        SnapshotHz = snapshotHz;
        SnapshotIntervalTicks = simulationHz / snapshotHz;
        _connectionTimeoutTicks = (uint)(simulationHz * 30);
    }

    public int Port => _transport.Port;
    public int SimulationHz { get; }
    public int SnapshotHz { get; }
    public int SnapshotIntervalTicks { get; }

    public void Start()
    {
        _transport.Start();
        _loopTask = Task.Run(MainLoopAsync);
        Console.WriteLine($"[Server] UDP listening on 0.0.0.0:{Port}");
    }

    private async Task MainLoopAsync()
    {
        var tickInterval = TimeSpan.FromSeconds(1.0 / SimulationHz);
        var stopwatch = Stopwatch.StartNew();
        var nextTickAt = stopwatch.Elapsed;

        while (!_cts.IsCancellationRequested)
        {
            DrainCharacterProfileResults();
            DrainNetworkInbox();
            EvictInactiveConnections();
            _overworldZone.Step();
            EnqueueLootPersistence(
                _overworldZone.ZoneKind,
                _overworldZone.InstanceId,
                _overworldZone.LastLootGrantEvents,
                _overworldZone.TryResolveClientIdByEntity);
            foreach (var dungeon in _dungeonInstances.Values)
            {
                dungeon.Step();
                EnqueueLootPersistence(
                    ZoneKind.Dungeon,
                    dungeon.InstanceId,
                    dungeon.LastLootGrantEvents,
                    dungeon.TryResolveClientIdByEntity);
            }

            _tick++;

            if (_tick % SnapshotIntervalTicks == 0)
            {
                BroadcastSnapshot();
            }

            nextTickAt += tickInterval;
            var delay = nextTickAt - stopwatch.Elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _cts.Token);
            }
            else
            {
                nextTickAt = stopwatch.Elapsed;
            }
        }
    }

    private void DrainNetworkInbox()
    {
        while (_transport.TryDequeue(out var datagram))
        {
            if (!ProtocolCodec.TryDecode(datagram.Payload, out var message) || message is null)
            {
                continue;
            }

            var endpointKey = datagram.Endpoint.ToString();
            if (_connectionsByEndpoint.TryGetValue(endpointKey, out var existing))
            {
                existing.LastSeenTick = _tick;
            }

            switch (message)
            {
                case ClientHello:
                    HandleClientHello(endpointKey, datagram.Endpoint);
                    break;
                case JoinOverworldRequest joinRequest:
                    HandleJoinOverworldRequest(endpointKey, datagram.Endpoint, joinRequest);
                    break;
                case JoinDungeonRequest:
                    HandleJoinDungeonRequest(endpointKey, datagram.Endpoint);
                    break;
                case DisconnectRequest:
                    RemoveConnectionAndEntity(endpointKey, "client disconnect");
                    break;
                case InputCommand input:
                    HandleInput(endpointKey, datagram.Endpoint, input);
                    break;
            }
        }
    }

    private void HandleClientHello(string endpointKey, IPEndPoint endpoint)
    {
        if (!_connectionsByEndpoint.TryGetValue(endpointKey, out var state))
        {
            state = new ConnectionState
            {
                ClientId = _nextClientId++,
                Endpoint = endpoint,
                LastSeenTick = _tick
            };
            _connectionsByEndpoint[endpointKey] = state;
        }
        else
        {
            state.LastSeenTick = _tick;
        }

        var serverHello = new ServerHello { AssignedClientId = state.ClientId };
        _transport.Send(endpoint, ProtocolCodec.Encode(serverHello));
    }

    private void HandleJoinOverworldRequest(string endpointKey, IPEndPoint endpoint, JoinOverworldRequest request)
    {
        if (!_connectionsByEndpoint.TryGetValue(endpointKey, out var state))
        {
            return;
        }

        state.CharacterName = request.CharacterName;
        state.AccountSubject = request.AccountSubject;
        state.AccountDisplayName = request.AccountDisplayName;
        state.CharacterSlot = Math.Clamp(request.CharacterSlot, 0, 7);
        state.CharacterId = ComputeStableCharacterId(state.AccountSubject, state.CharacterSlot);

        if (state.ZoneKind == ZoneKind.Dungeon && state.DungeonInstanceId != 0 && _dungeonInstances.TryGetValue(state.DungeonInstanceId, out var currentDungeon))
        {
            if (currentDungeon.TryTransferOut(state.ClientId, out var transfer))
            {
                EnqueueProfileSave(state, transfer);
                state.EntityId = _overworldZone.JoinTransferred(state.ClientId, transfer);
            }
            else
            {
                state.EntityId = _overworldZone.Join(state.ClientId, request.CharacterName);
            }

            state.ZoneKind = ZoneKind.Overworld;
            state.DungeonInstanceId = 0;
        }
        else if (!state.Joined)
        {
            if (_characterProfileService is not null)
            {
                if (_pendingJoinRequests.ContainsKey(endpointKey))
                {
                    return;
                }

                var queued = _characterProfileService.TryEnqueueLoad(new CharacterProfileLoadRequest(
                    EndpointKey: endpointKey,
                    ClientId: state.ClientId,
                    AccountSubject: state.AccountSubject,
                    AccountDisplayName: state.AccountDisplayName,
                    CharacterSlot: state.CharacterSlot,
                    PreferredCharacterName: request.CharacterName));

                if (queued)
                {
                    _pendingJoinRequests[endpointKey] = new PendingJoinRequest
                    {
                        Endpoint = endpoint
                    };
                    return;
                }
            }

            JoinOverworldImmediately(state, endpoint, null);
            return;
        }

        Console.WriteLine($"[Server] Client {state.ClientId} joined Overworld as entity {state.EntityId}. Players={_overworldZone.PlayerCount}");
        var accepted = new JoinOverworldAccepted
        {
            EntityId = state.EntityId,
            PlayerCount = (ushort)_overworldZone.PlayerCount,
            ZoneKind = ZoneKind.Overworld
        };

        _transport.Send(endpoint, ProtocolCodec.Encode(accepted));
    }

    private void DrainCharacterProfileResults()
    {
        if (_characterProfileService is null)
        {
            return;
        }

        while (_characterProfileService.TryDequeueLoaded(out var result))
        {
            if (!_pendingJoinRequests.TryGetValue(result.EndpointKey, out var pending))
            {
                continue;
            }

            if (!_connectionsByEndpoint.TryGetValue(result.EndpointKey, out var state) || state.Joined)
            {
                _pendingJoinRequests.Remove(result.EndpointKey);
                continue;
            }

            if (result.CharacterId != Guid.Empty)
            {
                state.CharacterId = result.CharacterId;
            }

            if (!string.IsNullOrWhiteSpace(result.CharacterName))
            {
                state.CharacterName = result.CharacterName;
            }

            JoinOverworldImmediately(state, pending.Endpoint, result.Profile);
            _pendingJoinRequests.Remove(result.EndpointKey);
        }
    }

    private void JoinOverworldImmediately(ConnectionState state, IPEndPoint endpoint, CharacterProfileData? profile)
    {
        state.EntityId = _overworldZone.Join(state.ClientId, state.CharacterName);
        if (profile is not null)
        {
            _ = _overworldZone.TryApplyPersistentProfile(state.ClientId, profile);
        }

        state.Joined = true;
        state.ZoneKind = ZoneKind.Overworld;

        Console.WriteLine($"[Server] Client {state.ClientId} joined Overworld as entity {state.EntityId}. Players={_overworldZone.PlayerCount}");

        var accepted = new JoinOverworldAccepted
        {
            EntityId = state.EntityId,
            PlayerCount = (ushort)_overworldZone.PlayerCount,
            ZoneKind = ZoneKind.Overworld
        };

        _transport.Send(endpoint, ProtocolCodec.Encode(accepted));
    }

    private void HandleJoinDungeonRequest(string endpointKey, IPEndPoint endpoint)
    {
        if (!_connectionsByEndpoint.TryGetValue(endpointKey, out var state) || !state.Joined)
        {
            return;
        }

        if (state.ZoneKind == ZoneKind.Overworld)
        {
            if (!_overworldZone.CanUseDungeonPortal(state.ClientId))
            {
                return;
            }

            if (!_overworldZone.TryTransferOut(state.ClientId, out var transfer))
            {
                return;
            }

            EnqueueProfileSave(state, transfer);
            var instanceId = _nextDungeonInstanceId++;
            var dungeon = new DungeonInstance(instanceId, SimulationHz);
            _dungeonInstances[instanceId] = dungeon;

            state.EntityId = dungeon.JoinTransferred(state.ClientId, transfer);
            state.ZoneKind = ZoneKind.Dungeon;
            state.DungeonInstanceId = instanceId;

            Console.WriteLine($"[Server] Client {state.ClientId} entered Dungeon instance {instanceId} as entity {state.EntityId}");

            var accepted = new JoinDungeonAccepted
            {
                EntityId = state.EntityId,
                DungeonInstanceId = instanceId,
                ZoneKind = ZoneKind.Dungeon
            };
            _transport.Send(endpoint, ProtocolCodec.Encode(accepted));
        }
    }

    private void HandleInput(string endpointKey, IPEndPoint endpoint, InputCommand input)
    {
        if (!_connectionsByEndpoint.TryGetValue(endpointKey, out var state) || !state.Joined)
        {
            return;
        }

        state.LastProcessedInputSequence = input.Sequence;

        if (state.ZoneKind == ZoneKind.Overworld)
        {
            _overworldZone.ApplyInput(state.ClientId, input.MoveX, input.MoveY, input.ActionFlags);

            if ((input.ActionFlags & InputActionFlags.InteractPortal) != 0)
            {
                HandleJoinDungeonRequest(endpointKey, endpoint);
            }
        }
        else if (state.ZoneKind == ZoneKind.Dungeon && state.DungeonInstanceId != 0 && _dungeonInstances.TryGetValue(state.DungeonInstanceId, out var dungeon))
        {
            dungeon.ApplyInput(state.ClientId, input.MoveX, input.MoveY, input.ActionFlags);
        }
    }

    private void BroadcastSnapshot()
    {
        foreach (var connection in _connectionsByEndpoint.Values)
        {
            if (!connection.Joined)
            {
                continue;
            }

            WorldSnapshot snapshot;
            if (connection.ZoneKind == ZoneKind.Dungeon && connection.DungeonInstanceId != 0 && _dungeonInstances.TryGetValue(connection.DungeonInstanceId, out var dungeon))
            {
                snapshot = dungeon.BuildSnapshot(_tick);
            }
            else
            {
                snapshot = _overworldZone.BuildSnapshot(_tick);
            }

            snapshot.LastProcessedInputSequence = connection.LastProcessedInputSequence;
            _transport.Send(connection.Endpoint, ProtocolCodec.Encode(snapshot));
        }
    }

    private void EnqueueLootPersistence(
        ZoneKind zoneKind,
        uint instanceId,
        IReadOnlyList<Armament.SharedSim.Sim.SimLootGrantEvent> events,
        TryResolveClientByEntityId resolveClientByEntityId)
    {
        if (_lootPersistenceSink is null || events.Count == 0)
        {
            return;
        }

        for (var i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            if (evt.CurrencyAmount <= 0 || !resolveClientByEntityId(evt.PlayerEntityId, out var clientId))
            {
                continue;
            }

            if (!TryGetConnectionByClientId(clientId, out var connection) || connection.CharacterId == Guid.Empty)
            {
                continue;
            }

            var request = new LootPersistenceRequest(
                CharacterId: connection.CharacterId,
                CharacterName: connection.CharacterName,
                ClientId: connection.ClientId,
                PlayerEntityId: evt.PlayerEntityId,
                LootId: evt.LootId,
                CurrencyDelta: evt.CurrencyAmount,
                AutoLoot: evt.AutoLoot,
                ServerTick: _tick,
                InstanceId: instanceId,
                ZoneName: zoneKind.ToString());

            _lootPersistenceSink.TryEnqueue(request);
        }
    }

    private bool TryGetConnectionByClientId(uint clientId, out ConnectionState state)
    {
        foreach (var candidate in _connectionsByEndpoint.Values)
        {
            if (candidate.ClientId == clientId)
            {
                state = candidate;
                return true;
            }
        }

        state = default!;
        return false;
    }

    private void EnqueueProfileSave(ConnectionState state, in PlayerTransferState transfer)
    {
        if (_characterProfileService is null || state.CharacterId == Guid.Empty)
        {
            return;
        }

        var profile = new CharacterProfileData(
            Level: transfer.Level,
            Experience: transfer.Experience,
            Currency: transfer.Currency,
            Attributes: transfer.Attributes);

        _ = _characterProfileService.TryEnqueueSave(new CharacterProfileSaveRequest(
            CharacterId: state.CharacterId,
            CharacterName: state.CharacterName,
            Profile: profile));
    }

    private void EvictInactiveConnections()
    {
        if (_connectionsByEndpoint.Count == 0 || _connectionTimeoutTicks == 0)
        {
            return;
        }

        var stale = new List<string>();
        foreach (var kvp in _connectionsByEndpoint)
        {
            if (_tick - kvp.Value.LastSeenTick >= _connectionTimeoutTicks)
            {
                stale.Add(kvp.Key);
            }
        }

        for (var i = 0; i < stale.Count; i++)
        {
            RemoveConnectionAndEntity(stale[i], "timeout");
        }
    }

    private void RemoveConnectionAndEntity(string endpointKey, string reason)
    {
        if (!_connectionsByEndpoint.TryGetValue(endpointKey, out var state))
        {
            return;
        }

        if (state.Joined)
        {
            if (state.ZoneKind == ZoneKind.Dungeon && state.DungeonInstanceId != 0 && _dungeonInstances.TryGetValue(state.DungeonInstanceId, out var dungeon))
            {
                _ = dungeon.RemoveClient(state.ClientId);
                if (dungeon.PlayerCount == 0)
                {
                    _dungeonInstances.Remove(state.DungeonInstanceId);
                }
            }
            else
            {
                _ = _overworldZone.RemoveClient(state.ClientId);
            }
        }

        _pendingJoinRequests.Remove(endpointKey);
        _connectionsByEndpoint.Remove(endpointKey);
        Console.WriteLine($"[Server] Client {state.ClientId} removed ({reason}).");
    }

    private static Guid ComputeStableCharacterId(string accountSubject, int characterSlot)
    {
        if (string.IsNullOrWhiteSpace(accountSubject))
        {
            return Guid.Empty;
        }

        var slot = Math.Clamp(characterSlot, 0, 7);
        var normalized = accountSubject.Trim().ToLowerInvariant();
        var data = System.Text.Encoding.UTF8.GetBytes($"armament-character:{normalized}:{slot}");
        var hash = System.Security.Cryptography.SHA256.HashData(data);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _transport.DisposeAsync();
        _cts.Dispose();
    }

    private sealed class ConnectionState
    {
        public required uint ClientId { get; init; }
        public required IPEndPoint Endpoint { get; init; }
        public Guid CharacterId { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public string AccountSubject { get; set; } = "local:guest";
        public string AccountDisplayName { get; set; } = "Guest";
        public int CharacterSlot { get; set; }
        public bool Joined { get; set; }
        public uint EntityId { get; set; }
        public uint LastProcessedInputSequence { get; set; }
        public uint LastSeenTick { get; set; }
        public ZoneKind ZoneKind { get; set; } = ZoneKind.Overworld;
        public uint DungeonInstanceId { get; set; }
    }

    private delegate bool TryResolveClientByEntityId(uint entityId, out uint clientId);

    private sealed class PendingJoinRequest
    {
        public required IPEndPoint Endpoint { get; init; }
    }
}
