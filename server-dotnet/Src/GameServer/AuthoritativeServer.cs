using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Armament.GameServer.Campaign;
using Armament.GameServer.Inventory;
using Armament.GameServer.Persistence;
using Armament.Net;
using Armament.SharedSim.Inventory;
using Armament.SharedSim.Protocol;
using Armament.SharedSim.Sim;

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
    private readonly LoadedAbilityProfiles _abilityProfiles;
    private readonly AuthoritativeInventoryService _inventoryService;
    private readonly CampPerimeterRuntime? _campPerimeterRuntime;
    private readonly CampaignWorldDefinition? _campaignWorldDefinition;
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
        ICharacterProfileService? characterProfileService = null,
        LoadedAbilityProfiles? abilityProfiles = null,
        IReadOnlyDictionary<string, CampaignEncounterDefinition>? campPerimeterDefinitions = null,
        CampaignWorldDefinition? campaignWorldDefinition = null)
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
        _abilityProfiles = abilityProfiles ?? new LoadedAbilityProfiles(
            new Dictionary<string, SimAbilityProfile>(StringComparer.Ordinal)
            {
                [SimAbilityProfiles.BuiltinV1.Id] = SimAbilityProfiles.BuiltinV1
            },
            BuildDefaultZoneDefinitions(),
            BuildDefaultLinkDefinitions(),
            SimAbilityProfiles.BuiltinV1.Id,
            "builtin profile only");
        _campaignWorldDefinition = campaignWorldDefinition;
        _overworldZone = new OverworldZone(
            simulationHz,
            _abilityProfiles.BySpecId,
            _abilityProfiles.ZoneDefinitions,
            _abilityProfiles.LinkDefinitions,
            _abilityProfiles.FallbackSpecId,
            campaignWorldDefinition);
        _inventoryService = new AuthoritativeInventoryService(BuildInventoryCatalog(campPerimeterDefinitions));
        _campPerimeterRuntime = campPerimeterDefinitions is { Count: > 0 }
            ? new CampPerimeterRuntime(campPerimeterDefinitions, campaignWorldDefinition?.Quests)
            : null;
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
            try
            {
                DrainCharacterProfileResults();
                DrainNetworkInbox();
                EvictInactiveConnections();
                _overworldZone.Step();
                ProcessOverworldCampaignRuntime();
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Tick error at {_tick}: {ex.GetType().Name} {ex.Message}");
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
        state.BaseClassId = request.BaseClassId;
        state.SpecId = request.SpecId;
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
                state.EntityId = _overworldZone.Join(state.ClientId, request.CharacterName, ResolveAndApplyProfileIdForConnection(state));
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
                    PreferredCharacterName: request.CharacterName,
                    RequestedBaseClassId: state.BaseClassId,
                    RequestedSpecId: state.SpecId));

                if (queued)
                {
                    _pendingJoinRequests[endpointKey] = new PendingJoinRequest
                    {
                        Endpoint = endpoint,
                        RequestedBaseClassId = state.BaseClassId,
                        RequestedSpecId = state.SpecId
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
            ZoneKind = ZoneKind.Overworld,
            BaseClassId = state.BaseClassId,
            SpecId = state.SpecId
        };

        _transport.Send(endpoint, ProtocolCodec.Encode(accepted));
    }

    private void DrainCharacterProfileResults()
    {
        var profileService = _characterProfileService;
        if (profileService is null)
        {
            return;
        }

        while (profileService.TryDequeueLoaded(out var result))
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
            if (result.Profile is not null)
            {
                var loadedProfile = result.Profile;
                var loadedAvailable = _abilityProfiles.BySpecId.ContainsKey(loadedProfile.SpecId);
                var requestedAvailable = _abilityProfiles.BySpecId.ContainsKey(pending.RequestedSpecId);

                if (requestedAvailable && !string.Equals(loadedProfile.SpecId, pending.RequestedSpecId, StringComparison.Ordinal))
                {
                    var reason = loadedAvailable
                        ? "requested spec differs from persisted profile"
                        : "persisted spec unavailable";
                    Console.WriteLine(
                        $"[Server] Profile spec sync for client {state.ClientId}: persisted '{loadedProfile.SpecId}' -> requested '{pending.RequestedSpecId}' ({reason}).");
                    state.BaseClassId = pending.RequestedBaseClassId;
                    state.SpecId = pending.RequestedSpecId;

                    if (_characterProfileService is not null && state.CharacterId != Guid.Empty)
                    {
                        var correctedProfile = new CharacterProfileData(
                            loadedProfile.Level,
                            loadedProfile.Experience,
                            loadedProfile.Currency,
                            loadedProfile.Attributes,
                            state.BaseClassId,
                            state.SpecId,
                            loadedProfile.InventoryJson,
                            loadedProfile.QuestProgressJson);
                        state.LastKnownProfile = correctedProfile;

                        _ = _characterProfileService.TryEnqueueSave(new CharacterProfileSaveRequest(
                            state.CharacterId,
                            state.CharacterName,
                            correctedProfile));
                    }
                }
                else
                {
                    state.BaseClassId = loadedProfile.BaseClassId;
                    state.SpecId = loadedProfile.SpecId;
                    state.LastKnownProfile = loadedProfile;
                }
            }

            JoinOverworldImmediately(state, pending.Endpoint, result.Profile);
            _pendingJoinRequests.Remove(result.EndpointKey);
        }
    }

    private void JoinOverworldImmediately(ConnectionState state, IPEndPoint endpoint, CharacterProfileData? profile)
    {
        CharacterProfileData? profileToApply = null;
        if (profile is not null)
        {
            profileToApply = profile with
            {
                BaseClassId = state.BaseClassId,
                SpecId = state.SpecId
            };
        }

        state.EntityId = _overworldZone.Join(state.ClientId, state.CharacterName, ResolveAndApplyProfileIdForConnection(state));
        if (profileToApply is not null)
        {
            _ = _overworldZone.TryApplyPersistentProfile(state.ClientId, profileToApply);
            _inventoryService.UpsertFromJson(state.CharacterId, profileToApply.InventoryJson);
            state.LastKnownProfile = profileToApply;
        }
        else
        {
            _inventoryService.GetOrCreate(state.CharacterId);
            state.LastKnownProfile = state.LastKnownProfile with
            {
                BaseClassId = state.BaseClassId,
                SpecId = state.SpecId,
                InventoryJson = _inventoryService.ExportJson(state.CharacterId)
            };
        }

        if (_campPerimeterRuntime is not null && state.CharacterId != Guid.Empty)
        {
            _campPerimeterRuntime.RestoreCharacterState(state.CharacterId, state.LastKnownProfile.QuestProgressJson);
        }

        state.Joined = true;
        state.ZoneKind = ZoneKind.Overworld;

        Console.WriteLine($"[Server] Client {state.ClientId} joined Overworld as entity {state.EntityId}. Players={_overworldZone.PlayerCount}");

        var accepted = new JoinOverworldAccepted
        {
            EntityId = state.EntityId,
            PlayerCount = (ushort)_overworldZone.PlayerCount,
            ZoneKind = ZoneKind.Overworld,
            BaseClassId = state.BaseClassId,
            SpecId = state.SpecId
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
            var dungeon = new DungeonInstance(
                instanceId,
                SimulationHz,
                _abilityProfiles.BySpecId,
                _abilityProfiles.ZoneDefinitions,
                _abilityProfiles.LinkDefinitions,
                _abilityProfiles.FallbackSpecId);
            _dungeonInstances[instanceId] = dungeon;

            state.EntityId = dungeon.JoinTransferred(state.ClientId, transfer);
            state.ZoneKind = ZoneKind.Dungeon;
            state.DungeonInstanceId = instanceId;

            Console.WriteLine($"[Server] Client {state.ClientId} entered Dungeon instance {instanceId} as entity {state.EntityId}");

            var accepted = new JoinDungeonAccepted
            {
                EntityId = state.EntityId,
                DungeonInstanceId = instanceId,
                ZoneKind = ZoneKind.Dungeon,
                BaseClassId = state.BaseClassId,
                SpecId = state.SpecId
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

            if ((input.ActionFlags & (InputActionFlags.InteractPortal | InputActionFlags.Interact)) != 0)
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
            if (_campPerimeterRuntime is not null && connection.CharacterId != Guid.Empty)
            {
                var objectives = _campPerimeterRuntime.BuildObjectiveSnapshots(connection.CharacterId);
                for (var i = 0; i < objectives.Count; i++)
                {
                    var objective = objectives[i];
                    snapshot.Objectives.Add(new WorldObjectiveSnapshot
                    {
                        ObjectiveId = objective.ObjectiveId,
                        EncounterId = objective.EncounterId,
                        Kind = objective.Kind,
                        TargetId = objective.TargetId,
                        Current = (ushort)Math.Clamp(objective.Current, 0, ushort.MaxValue),
                        Required = (ushort)Math.Clamp(objective.Required, 0, ushort.MaxValue),
                        State = (byte)objective.State
                    });
                }
            }
            _transport.Send(connection.Endpoint, ProtocolCodec.Encode(snapshot));
        }
    }

    private void ProcessOverworldCampaignRuntime()
    {
        if (_campPerimeterRuntime is null)
        {
            return;
        }

        var completions = _campPerimeterRuntime.Consume(
            _overworldZone.LastSimEvents,
            ResolveCharacterIdByPlayerEntity,
            _overworldZone.TryResolveObjectDefIdByRuntimeObjectId,
            _overworldZone.TryResolveNpcIdByRuntimeNpcId);

        for (var i = 0; i < completions.Count; i++)
        {
            var completion = completions[i];
            if (!string.IsNullOrWhiteSpace(completion.RewardItemCode))
            {
                var result = _inventoryService.GrantItem(completion.CharacterId, completion.RewardItemCode!, completion.RewardItemQuantity);
                Console.WriteLine(
                    $"[Server] Campaign completion: char={completion.CharacterId} encounter={completion.EncounterId} reward={completion.RewardItemCode}x{completion.RewardItemQuantity} inventory={(result.Success ? "ok" : result.Message)}");
            }
            else
            {
                Console.WriteLine($"[Server] Campaign completion: char={completion.CharacterId} encounter={completion.EncounterId} (no item reward)");
            }

            TryPersistCharacterProfile(completion.CharacterId);
        }

        var activeEncounterIds = _campPerimeterRuntime.BuildActiveEncounterIds(BuildOnlineCharacterIds());
        _overworldZone.SyncActivatedEncounters(activeEncounterIds);

        var dirtyCharacterIds = _campPerimeterRuntime.ConsumeDirtyCharacterIds();
        foreach (var characterId in dirtyCharacterIds)
        {
            TryPersistCharacterProfile(characterId);
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

    private bool TryGetConnectionByCharacterId(Guid characterId, out ConnectionState state)
    {
        foreach (var candidate in _connectionsByEndpoint.Values)
        {
            if (candidate.CharacterId == characterId)
            {
                state = candidate;
                return true;
            }
        }

        state = default!;
        return false;
    }

    private Guid? ResolveCharacterIdByPlayerEntity(uint playerEntityId)
    {
        if (!_overworldZone.TryResolveClientIdByEntity(playerEntityId, out var clientId))
        {
            return null;
        }

        if (!TryGetConnectionByClientId(clientId, out var connection) || connection.CharacterId == Guid.Empty)
        {
            return null;
        }

        return connection.CharacterId;
    }

    private IReadOnlyCollection<Guid> BuildOnlineCharacterIds()
    {
        var ids = new HashSet<Guid>();
        foreach (var connection in _connectionsByEndpoint.Values)
        {
            if (!connection.Joined || connection.CharacterId == Guid.Empty)
            {
                continue;
            }

            ids.Add(connection.CharacterId);
        }

        return ids;
    }

    private void TryPersistCharacterProfile(Guid characterId)
    {
        if (_characterProfileService is null || characterId == Guid.Empty)
        {
            return;
        }

        if (!TryGetConnectionByCharacterId(characterId, out var connection))
        {
            return;
        }

        var inventoryJson = _inventoryService.ExportJson(characterId);
        var questProgressJson = _campPerimeterRuntime?.ExportCharacterState(characterId) ?? connection.LastKnownProfile.QuestProgressJson;
        var profile = new CharacterProfileData(
            Level: connection.LastKnownProfile.Level,
            Experience: connection.LastKnownProfile.Experience,
            Currency: connection.LastKnownProfile.Currency,
            Attributes: connection.LastKnownProfile.Attributes,
            BaseClassId: connection.BaseClassId,
            SpecId: connection.SpecId,
            InventoryJson: inventoryJson,
            QuestProgressJson: questProgressJson);

        connection.LastKnownProfile = profile;
        _ = _characterProfileService.TryEnqueueSave(new CharacterProfileSaveRequest(
            CharacterId: characterId,
            CharacterName: connection.CharacterName,
            Profile: profile));
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
            Attributes: transfer.Attributes,
            BaseClassId: state.BaseClassId,
            SpecId: state.SpecId,
            InventoryJson: _inventoryService.ExportJson(state.CharacterId),
            QuestProgressJson: _campPerimeterRuntime?.ExportCharacterState(state.CharacterId) ?? state.LastKnownProfile.QuestProgressJson);

        state.LastKnownProfile = profile;

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

    private string ResolveAndApplyProfileIdForConnection(ConnectionState state)
    {
        var requested = state.SpecId;
        var resolved = _abilityProfiles.ResolveForSpec(requested).Id;
        if (!string.Equals(requested, resolved, StringComparison.Ordinal))
        {
            Console.WriteLine($"[Server] Spec fallback for client {state.ClientId}: requested '{requested}', resolved '{resolved}'.");
        }

        state.SpecId = resolved;
        var parsedClass = TryParseClassFromSpecId(resolved);
        if (!string.IsNullOrWhiteSpace(parsedClass))
        {
            state.BaseClassId = parsedClass;
        }

        return resolved;
    }

    private static string? TryParseClassFromSpecId(string specId)
    {
        if (string.IsNullOrWhiteSpace(specId))
        {
            return null;
        }

        var parts = specId.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && string.Equals(parts[0], "spec", StringComparison.OrdinalIgnoreCase))
        {
            return parts[1].ToLowerInvariant();
        }

        return null;
    }

    private static Dictionary<string, SimZoneDefinition> BuildDefaultZoneDefinitions()
    {
        var byId = new Dictionary<string, SimZoneDefinition>(StringComparer.Ordinal);
        foreach (var zone in SimZoneLinkDefaults.Zones)
        {
            byId[zone.Id] = zone;
        }

        return byId;
    }

    private static Dictionary<string, SimLinkDefinition> BuildDefaultLinkDefinitions()
    {
        var byId = new Dictionary<string, SimLinkDefinition>(StringComparer.Ordinal);
        foreach (var link in SimZoneLinkDefaults.Links)
        {
            byId[link.Id] = link;
        }

        return byId;
    }

    private static IInventoryItemCatalog BuildInventoryCatalog(IReadOnlyDictionary<string, CampaignEncounterDefinition>? definitions)
    {
        var byCode = new Dictionary<string, InventoryItemDefinition>(StringComparer.Ordinal)
        {
            ["item.quest.camp_supply"] = new InventoryItemDefinition { ItemCode = "item.quest.camp_supply", MaxStack = 99 },
            ["item.quest.supply_cache"] = new InventoryItemDefinition { ItemCode = "item.quest.supply_cache", MaxStack = 99 },
            ["item.quest.plot_seal"] = new InventoryItemDefinition { ItemCode = "item.quest.plot_seal", MaxStack = 99 }
        };

        if (definitions is not null)
        {
            foreach (var def in definitions.Values)
            {
                if (string.IsNullOrWhiteSpace(def.RewardItemCode))
                {
                    continue;
                }

                if (!byCode.ContainsKey(def.RewardItemCode))
                {
                    byCode[def.RewardItemCode] = new InventoryItemDefinition
                    {
                        ItemCode = def.RewardItemCode,
                        MaxStack = 99
                    };
                }
            }
        }

        return new DictionaryInventoryItemCatalog(byCode);
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
        public string BaseClassId { get; set; } = "bastion";
        public string SpecId { get; set; } = "spec.bastion.bulwark";
        public CharacterProfileData LastKnownProfile { get; set; } = new(
            Level: 1,
            Experience: 0,
            Currency: 0,
            Attributes: CharacterAttributes.Default,
            BaseClassId: "bastion",
            SpecId: "spec.bastion.bulwark",
            InventoryJson: "{}");
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
        public required string RequestedBaseClassId { get; init; }
        public required string RequestedSpecId { get; init; }
    }
}
