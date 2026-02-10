#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Armament.SharedSim.Protocol;
using Armament.SharedSim.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Armament.Client.Networking;

public sealed class UdpGameClient : MonoBehaviour
{
    [SerializeField] private string host = "127.0.0.1";
    [SerializeField] private int port = 9000;
    [SerializeField] private string accountSubject = "local:dev-account";
    [SerializeField] private string accountDisplayName = "DevAccount";
    [SerializeField] private int characterSlot;
    [SerializeField] private string characterName = "Player";
    [SerializeField] private bool connectOnStart = true;

    private const int SimulationHz = 60;
    private const int InputScale = 1000;
    private const int WorldBoundaryMilli = 500_000;
    private const float InterpolationBackTimeSeconds = 0.10f;
    private const float PendingLootHideTimeoutSeconds = 1.5f;
    private const uint PortalEntityId = 900_001;

    private readonly ConcurrentQueue<IProtocolMessage> inbox = new();
    private readonly Dictionary<uint, Vector2> latestEntities = new();
    private readonly Dictionary<uint, Vector2> renderEntities = new();
    private readonly Dictionary<uint, EntityKind> latestEntityKinds = new();
    private readonly Dictionary<uint, ushort> lootCurrencyById = new();
    private readonly Dictionary<uint, List<SnapshotSample>> remoteEntitySamples = new();
    private readonly List<PendingInput> pendingInputs = new();
    private readonly List<uint> entityCleanupBuffer = new();
    private readonly Dictionary<uint, float> pendingLootHideUntilSeconds = new();

    private UdpClient? udpClient;
    private IPEndPoint? serverEndpoint;
    private CancellationTokenSource? cts;

    private uint localEntityId;
    private uint currentInstanceId;
    private uint inputSequence;
    private int pickupIntentTicksRemaining;
    private bool pickupPressedBuffered;
    private bool interactPressedBufferedForZone;
    private bool interactPressedBufferedForSim;
    private bool returnHomePressedBuffered;
    private bool lootNamesToggleBuffered;
    private bool joined;
    private uint localClientTick;
    private float localTickAccumulator;
    private bool hasLocalPredictionState;
    private int predictedLocalXMilli;
    private int predictedLocalYMilli;
    private readonly int moveSpeedMilliPerSecond;

    public event Action? SnapshotReceived;

    public IReadOnlyDictionary<uint, Vector2> LatestEntities => latestEntities;
    public IReadOnlyDictionary<uint, Vector2> RenderEntities => renderEntities;
    public IReadOnlyDictionary<uint, EntityKind> LatestEntityKinds => latestEntityKinds;
    public IReadOnlyDictionary<uint, ushort> LootCurrencyById => lootCurrencyById;
    public uint LocalEntityId => localEntityId;
    public bool HasReceivedSnapshot { get; private set; }
    public ushort LocalHealth { get; private set; }
    public ushort LocalBuilderResource { get; private set; }
    public ushort LocalSpenderResource { get; private set; }
    public ushort LocalCurrency { get; private set; }
    public bool ShowLootNames { get; private set; }
    public ZoneKind CurrentZone { get; private set; } = ZoneKind.Overworld;
    public uint CurrentInstanceId => currentInstanceId;
    public string AccountSubject => accountSubject;
    public string AccountDisplayName => accountDisplayName;
    public int CharacterSlot => characterSlot;
    public bool IsConnected => udpClient is not null;
    public bool IsJoined => joined;

    public UdpGameClient()
    {
        var derived = CharacterMath.ComputeDerived(CharacterAttributes.Default, CharacterStatTuning.Default);
        moveSpeedMilliPerSecond = derived.MoveSpeedMilliPerSecond;
    }

    public void Configure(string serverHost, int serverPort, string name, string? subject = null, int slot = 0, string? displayName = null)
    {
        host = serverHost;
        port = serverPort;
        characterName = name;
        if (!string.IsNullOrWhiteSpace(subject))
        {
            accountSubject = subject;
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            accountDisplayName = displayName;
        }

        characterSlot = Mathf.Clamp(slot, 0, 7);
    }

    public void SetAutoConnect(bool enabled)
    {
        connectOnStart = enabled;
    }

    private async void Start()
    {
        if (connectOnStart)
        {
            await ConnectAsync();
        }
    }

    public async Task ConnectAsync()
    {
        if (udpClient is not null)
        {
            return;
        }

        cts = new CancellationTokenSource();
        udpClient = new UdpClient(0);
        serverEndpoint = new IPEndPoint(IPAddress.Parse(host), port);

        _ = ReceiveLoopAsync(cts.Token);

        await SendAsync(new ClientHello { ClientNonce = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue) });
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (udpClient is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync();
                if (ProtocolCodec.TryDecode(result.Buffer, out var message) && message is not null)
                {
                    inbox.Enqueue(message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
            }
        }
    }

    private async Task SendAsync(IProtocolMessage message)
    {
        if (udpClient is null || serverEndpoint is null)
        {
            return;
        }

        var payload = ProtocolCodec.Encode(message);
        await udpClient.SendAsync(payload, payload.Length, serverEndpoint);
    }

    private async void Update()
    {
        while (inbox.TryDequeue(out var message))
        {
            switch (message)
            {
                case ServerHello:
                    await SendAsync(BuildJoinRequest());
                    break;
                case JoinOverworldAccepted accepted:
                    localEntityId = accepted.EntityId;
                    joined = true;
                    CurrentZone = accepted.ZoneKind;
                    currentInstanceId = 0;
                    break;
                case JoinDungeonAccepted accepted:
                    localEntityId = accepted.EntityId;
                    joined = true;
                    CurrentZone = accepted.ZoneKind;
                    currentInstanceId = accepted.DungeonInstanceId;
                    break;
                case WorldSnapshot snapshot:
                    ApplySnapshot(snapshot);
                    break;
            }
        }

        if (!joined)
        {
            return;
        }

        BufferEdgeInputIntents();
        ToggleLootNamesIfRequested();
        HandleZoneTransitionHotkeys();
        BufferPickupKeyIntent();

        localTickAccumulator += Time.deltaTime;
        var fixedTickDelta = 1f / SimulationHz;

        while (localTickAccumulator >= fixedTickDelta)
        {
            localTickAccumulator -= fixedTickDelta;
            RunLocalPredictionTick();
        }

        BuildInterpolatedRenderState();
    }

    private void RunLocalPredictionTick()
    {
        var movement = ReadMovementInput();
        var actionFlags = ReadActionFlags();
        if (ConsumeBufferedPickupIntent())
        {
            actionFlags &= ~InputActionFlags.FastAttackHold;
            pickupIntentTicksRemaining = 10;
        }

        if (pickupIntentTicksRemaining > 0)
        {
            actionFlags |= InputActionFlags.Pickup;
            actionFlags &= ~InputActionFlags.FastAttackHold;
            pickupIntentTicksRemaining--;
        }
        var quantizedX = Quantization.QuantizeInput(movement.x);
        var quantizedY = Quantization.QuantizeInput(movement.y);

        localClientTick++;
        var sequence = ++inputSequence;

        pendingInputs.Add(new PendingInput
        {
            Sequence = sequence,
            ClientTick = localClientTick,
            MoveX = quantizedX,
            MoveY = quantizedY
        });

        if (!hasLocalPredictionState)
        {
            hasLocalPredictionState = true;
        }

        IntegrateLocal(quantizedX, quantizedY);
        renderEntities[localEntityId] = MilliToVector(predictedLocalXMilli, predictedLocalYMilli);

        _ = SendAsync(new InputCommand
        {
            Sequence = sequence,
            ClientTick = localClientTick,
            MoveX = quantizedX,
            MoveY = quantizedY,
            ActionFlags = actionFlags
        });
    }

    private void ApplySnapshot(WorldSnapshot snapshot)
    {
        latestEntities.Clear();
        latestEntityKinds.Clear();
        lootCurrencyById.Clear();
        CurrentZone = snapshot.ZoneKind;
        currentInstanceId = snapshot.InstanceId;

        foreach (var entity in snapshot.Entities)
        {
            var position = new Vector2(
                Quantization.DequantizePosition(entity.QuantizedX),
                Quantization.DequantizePosition(entity.QuantizedY));
            latestEntities[entity.EntityId] = position;
            latestEntityKinds[entity.EntityId] = entity.Kind;
            if (entity.Kind == EntityKind.Loot)
            {
                lootCurrencyById[entity.EntityId] = entity.Currency;
            }
        }

        ReconcilePendingLootVisibility();
        CleanupRemovedEntities();

        if (latestEntities.TryGetValue(localEntityId, out var authoritativeLocal))
        {
            predictedLocalXMilli = Mathf.RoundToInt(authoritativeLocal.x * 1000f);
            predictedLocalYMilli = Mathf.RoundToInt(authoritativeLocal.y * 1000f);
            hasLocalPredictionState = true;

            if (snapshot.LastProcessedInputSequence > 0)
            {
                pendingInputs.RemoveAll(x => x.Sequence <= snapshot.LastProcessedInputSequence);
            }

            foreach (var input in pendingInputs)
            {
                IntegrateLocal(input.MoveX, input.MoveY);
            }

            renderEntities[localEntityId] = MilliToVector(predictedLocalXMilli, predictedLocalYMilli);
        }

        for (var i = 0; i < snapshot.Entities.Count; i++)
        {
            var entity = snapshot.Entities[i];
            if (entity.EntityId == localEntityId)
            {
                LocalHealth = entity.Health;
                LocalBuilderResource = entity.BuilderResource;
                LocalSpenderResource = entity.SpenderResource;
                LocalCurrency = entity.Currency;
                break;
            }
        }

        var receivedAt = Time.unscaledTime;
        foreach (var entity in snapshot.Entities)
        {
            if (entity.EntityId == localEntityId || entity.Kind == EntityKind.Loot)
            {
                continue;
            }

            if (!remoteEntitySamples.TryGetValue(entity.EntityId, out var samples))
            {
                samples = new List<SnapshotSample>();
                remoteEntitySamples[entity.EntityId] = samples;
            }

            samples.Add(new SnapshotSample
            {
                ServerTick = snapshot.ServerTick,
                ReceivedAtSeconds = receivedAt,
                Position = latestEntities[entity.EntityId]
            });

            if (samples.Count > 32)
            {
                samples.RemoveRange(0, samples.Count - 32);
            }
        }

        HasReceivedSnapshot = true;
        SnapshotReceived?.Invoke();
    }

    private void CleanupRemovedEntities()
    {
        entityCleanupBuffer.Clear();

        foreach (var entityId in renderEntities.Keys)
        {
            if (entityId == localEntityId)
            {
                continue;
            }

            if (!latestEntityKinds.ContainsKey(entityId))
            {
                entityCleanupBuffer.Add(entityId);
            }
        }

        for (var i = 0; i < entityCleanupBuffer.Count; i++)
        {
            var entityId = entityCleanupBuffer[i];
            renderEntities.Remove(entityId);
            remoteEntitySamples.Remove(entityId);
            lootCurrencyById.Remove(entityId);
            pendingLootHideUntilSeconds.Remove(entityId);
        }
    }

    private void BuildInterpolatedRenderState()
    {
        var now = Time.unscaledTime;
        var targetTime = now - InterpolationBackTimeSeconds;

        foreach (var kvp in remoteEntitySamples)
        {
            var entityId = kvp.Key;
            var samples = kvp.Value;
            if (samples.Count == 0)
            {
                continue;
            }

            while (samples.Count >= 2 && samples[1].ReceivedAtSeconds <= targetTime)
            {
                samples.RemoveAt(0);
            }

            if (samples.Count >= 2)
            {
                var older = samples[0];
                var newer = samples[1];
                var span = Mathf.Max(0.0001f, newer.ReceivedAtSeconds - older.ReceivedAtSeconds);
                var t = Mathf.Clamp01((targetTime - older.ReceivedAtSeconds) / span);
                renderEntities[entityId] = Vector2.Lerp(older.Position, newer.Position, t);
            }
            else
            {
                renderEntities[entityId] = samples[0].Position;
            }
        }

        foreach (var kvp in latestEntityKinds)
        {
            if (kvp.Value != EntityKind.Loot)
            {
                continue;
            }

            if (latestEntities.TryGetValue(kvp.Key, out var pos))
            {
                if (pendingLootHideUntilSeconds.ContainsKey(kvp.Key))
                {
                    continue;
                }

                renderEntities[kvp.Key] = pos;
            }
        }
    }

    private void IntegrateLocal(short inputX, short inputY)
    {
        var deltaX = inputX * moveSpeedMilliPerSecond / (InputScale * SimulationHz);
        var deltaY = inputY * moveSpeedMilliPerSecond / (InputScale * SimulationHz);

        predictedLocalXMilli = Mathf.Clamp(predictedLocalXMilli + deltaX, -WorldBoundaryMilli, WorldBoundaryMilli);
        predictedLocalYMilli = Mathf.Clamp(predictedLocalYMilli + deltaY, -WorldBoundaryMilli, WorldBoundaryMilli);
    }

    private static Vector2 MilliToVector(int xMilli, int yMilli)
    {
        return new Vector2(xMilli / 1000f, yMilli / 1000f);
    }

    private static Vector2 ReadMovementInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard is null)
        {
            return Vector2.zero;
        }

        var x = 0f;
        var y = 0f;

        if (keyboard.aKey.isPressed) x -= 1f;
        if (keyboard.dKey.isPressed) x += 1f;
        if (keyboard.sKey.isPressed) y -= 1f;
        if (keyboard.wKey.isPressed) y += 1f;

        var vector = new Vector2(x, y);
        return vector.sqrMagnitude > 1f ? vector.normalized : vector;
    }

    private InputActionFlags ReadActionFlags()
    {
        var flags = InputActionFlags.None;
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        if (mouse is not null)
        {
            if (mouse.leftButton.isPressed) flags |= InputActionFlags.FastAttackHold;
            if (mouse.rightButton.isPressed) flags |= InputActionFlags.HeavyAttackHold;
        }

        if (keyboard is not null)
        {
            if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) flags |= InputActionFlags.BlockHold;
            if (keyboard.eKey.isPressed) flags |= InputActionFlags.Skill1;
            if (keyboard.rKey.isPressed) flags |= InputActionFlags.Skill2;
            if (keyboard.qKey.isPressed) flags |= InputActionFlags.Skill3;
            if (keyboard.tKey.isPressed) flags |= InputActionFlags.Skill4;
            if (keyboard.digit1Key.isPressed) flags |= InputActionFlags.Skill5;
            if (keyboard.digit2Key.isPressed) flags |= InputActionFlags.Skill6;
            if (keyboard.digit3Key.isPressed) flags |= InputActionFlags.Skill7;
            if (keyboard.digit4Key.isPressed) flags |= InputActionFlags.Skill8;
        }

        if (interactPressedBufferedForSim)
        {
            flags |= InputActionFlags.InteractPortal;
            interactPressedBufferedForSim = false;
        }

        return flags;
    }

    private void ToggleLootNamesIfRequested()
    {
        if (lootNamesToggleBuffered)
        {
            lootNamesToggleBuffered = false;
            ShowLootNames = !ShowLootNames;
        }
    }

    private void HandleZoneTransitionHotkeys()
    {
        if (CurrentZone == ZoneKind.Overworld && interactPressedBufferedForZone)
        {
            interactPressedBufferedForZone = false;
        }
        else if (CurrentZone == ZoneKind.Dungeon && returnHomePressedBuffered)
        {
            returnHomePressedBuffered = false;
            _ = SendAsync(BuildJoinRequest());
        }
        else
        {
            // If key was buffered in a zone where it's not applicable, drop it.
            interactPressedBufferedForZone = false;
            returnHomePressedBuffered = false;
        }
    }

    private void BufferEdgeInputIntents()
    {
        var keyboard = Keyboard.current;
        if (keyboard is null)
        {
            return;
        }

        if (keyboard.leftAltKey.wasPressedThisFrame || keyboard.rightAltKey.wasPressedThisFrame)
        {
            lootNamesToggleBuffered = true;
        }

        if (keyboard.fKey.wasPressedThisFrame)
        {
            interactPressedBufferedForZone = true;
            interactPressedBufferedForSim = true;
        }

        if (keyboard.hKey.wasPressedThisFrame)
        {
            returnHomePressedBuffered = true;
        }
    }

    private bool IsLootClickedThisTick()
    {
        var mouse = Mouse.current;
        if (mouse is null || !mouse.leftButton.wasPressedThisFrame)
        {
            return false;
        }

        var camera = Camera.main;
        if (camera is null)
        {
            return false;
        }

        var screen = mouse.position.ReadValue();
        var world = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, Mathf.Abs(camera.transform.position.z)));
        var click = new Vector2(world.x, world.y);

        const float clickRadius = 0.9f;
        var clickRadiusSq = clickRadius * clickRadius;

        var clickedEntityId = 0u;
        var closestDistSq = float.MaxValue;
        var mouseScreen = mouse.position.ReadValue();

        foreach (var kvp in latestEntityKinds)
        {
            if (kvp.Value != EntityKind.Loot || kvp.Key == PortalEntityId)
            {
                continue;
            }

            if (!latestEntities.TryGetValue(kvp.Key, out var pos))
            {
                continue;
            }

            var distSq = (pos - click).sqrMagnitude;
            var clickedWorld = distSq <= clickRadiusSq;
            var clickedLabel = IsClickingLootLabel(mouseScreen, pos, camera);
            if (clickedWorld || clickedLabel)
            {
                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    clickedEntityId = kvp.Key;
                }
            }
        }

        if (clickedEntityId == 0)
        {
            return false;
        }

        // Hide immediately for responsive feedback, then reconcile from authoritative snapshot.
        pendingLootHideUntilSeconds[clickedEntityId] = Time.unscaledTime + PendingLootHideTimeoutSeconds;
        renderEntities.Remove(clickedEntityId);
        return true;
    }

    private void BufferPickupKeyIntent()
    {
        if (pickupPressedBuffered || pickupIntentTicksRemaining > 0)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard is not null && keyboard.zKey.wasPressedThisFrame)
        {
            pickupPressedBuffered = true;
        }
    }

    private bool ConsumeBufferedPickupIntent()
    {
        if (!pickupPressedBuffered)
        {
            return false;
        }

        pickupPressedBuffered = false;
        return true;
    }

    private void ReconcilePendingLootVisibility()
    {
        entityCleanupBuffer.Clear();
        var now = Time.unscaledTime;

        foreach (var kvp in pendingLootHideUntilSeconds)
        {
            var entityId = kvp.Key;
            if (!latestEntityKinds.TryGetValue(entityId, out var kind) || kind != EntityKind.Loot)
            {
                // Loot disappeared in authoritative state; pickup confirmed.
                entityCleanupBuffer.Add(entityId);
                continue;
            }

            if (now >= kvp.Value)
            {
                // Server still reports the loot after timeout (likely out of range or denied), show it again.
                entityCleanupBuffer.Add(entityId);
            }
        }

        for (var i = 0; i < entityCleanupBuffer.Count; i++)
        {
            pendingLootHideUntilSeconds.Remove(entityCleanupBuffer[i]);
        }
    }

    private static bool IsClickingLootLabel(Vector2 mouseScreen, Vector2 lootWorldPos, Camera camera)
    {
        var screenPos = camera.WorldToScreenPoint(new Vector3(lootWorldPos.x, lootWorldPos.y + 0.9f, 0f));
        if (screenPos.z < 0f)
        {
            return false;
        }

        var width = 90f;
        var rect = new Rect(screenPos.x - width * 0.5f, Screen.height - screenPos.y - 22f, width, 20f);
        var guiPoint = new Vector2(mouseScreen.x, Screen.height - mouseScreen.y);
        return rect.Contains(guiPoint);
    }

    private void OnDestroy()
    {
        SendDisconnectBestEffort();
        cts?.Cancel();
        udpClient?.Dispose();
        cts?.Dispose();
    }

    private void SendDisconnectBestEffort()
    {
        if (udpClient is null || serverEndpoint is null)
        {
            return;
        }

        try
        {
            var payload = ProtocolCodec.Encode(new DisconnectRequest());
            _ = udpClient.Send(payload, payload.Length, serverEndpoint);
        }
        catch
        {
        }
    }

    private JoinOverworldRequest BuildJoinRequest()
    {
        return new JoinOverworldRequest
        {
            AccountSubject = string.IsNullOrWhiteSpace(accountSubject) ? "local:guest" : accountSubject.Trim(),
            AccountDisplayName = string.IsNullOrWhiteSpace(accountDisplayName) ? "Guest" : accountDisplayName.Trim(),
            CharacterSlot = Mathf.Clamp(characterSlot, 0, 7),
            CharacterName = characterName
        };
    }

    private struct PendingInput
    {
        public uint Sequence;
        public uint ClientTick;
        public short MoveX;
        public short MoveY;
    }

    private struct SnapshotSample
    {
        public uint ServerTick;
        public float ReceivedAtSeconds;
        public Vector2 Position;
    }
}
