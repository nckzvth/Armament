using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Armament.Client.MonoGame.Animation;
using Armament.SharedSim.Protocol;
using Armament.SharedSim.Sim;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Armament.Client.MonoGame;

public sealed class ArmamentGame : Game
{
    private const int SimulationHz = 60;
    private const int InputScale = 1000;
    private const int WorldBoundaryMilli = 500_000;
    private const float InterpolationBackTimeSeconds = 0.10f;
    private const int MaxFeedEntries = 10;
    private const int MaxSlots = 6;
    private const uint PortalEntityId = 900_001;
    private const float DefaultWorldZoom = 108f;

    private enum UiScreen
    {
        Login,
        CharacterSelect,
        CharacterCreation,
        Settings,
        InGame
    }

    private readonly GraphicsDeviceManager graphics;
    private SpriteBatch? spriteBatch;
    private SpriteFont? debugFont;
    private Texture2D? pixel;

    private readonly ClientConfig config;
    private readonly CharacterSlotStore slotStore;
    private readonly UdpProtocolClient netClient;

    private readonly Dictionary<uint, Vector2> latestEntities = new();
    private readonly Dictionary<uint, Vector2> renderEntities = new();
    private readonly Dictionary<uint, EntityKind> latestEntityKinds = new();
    private readonly Dictionary<uint, List<SnapshotSample>> remoteEntitySamples = new();
    private readonly Dictionary<uint, ushort> latestEntityHealth = new();
    private readonly Dictionary<uint, ushort> latestEntityBuilderResource = new();
    private readonly Dictionary<uint, ushort> latestEntitySpenderResource = new();
    private readonly Dictionary<uint, uint> latestLinkOwnerByEntity = new();
    private readonly Dictionary<uint, uint> latestLinkTargetByEntity = new();
    private readonly Dictionary<uint, ushort> latestLinkRemainingTicks = new();
    private readonly Dictionary<uint, uint> latestEnemyAggroTarget = new();
    private readonly Dictionary<uint, ushort> latestEnemyAggroThreat = new();
    private readonly Dictionary<uint, byte> latestEnemyForcedTicks = new();
    private readonly Dictionary<uint, byte> latestEnemyPrimaryStatusStacks = new();
    private readonly Dictionary<uint, ushort> lootCurrencyById = new();

    private readonly List<PendingInput> pendingInputs = new();
    private readonly ushort[] localCooldownTicks = new ushort[10];
    private readonly ushort[] localCooldownStartTicks = new ushort[10];
    private readonly List<string> combatFeed = new();
    private readonly List<string> castFeed = new();
    private readonly List<UiButton> uiButtons = new();

    private uint localEntityId;
    private uint currentInstanceId;
    private uint inputSequence;
    private uint localClientTick;
    private float localTickAccumulator;
    private bool joined;
    private bool hasSnapshot;
    private bool hasLocalPredictionState;
    private int predictedLocalXMilli;
    private int predictedLocalYMilli;
    private readonly int moveSpeedMilliPerSecond;
    private double totalGameSeconds;
    private float frameDeltaSeconds;
    private readonly string repoRoot;
    private AtlasAnimationRuntime? localAnimationRuntime;
    private LocalAtlasAnimator? localAnimator;
    private string loadedAnimationClassId = string.Empty;
    private string loadedAnimationSpecId = string.Empty;

    private ZoneKind currentZone = ZoneKind.Overworld;
    private InputActionFlags localActionFlags;
    private InputActionFlags previousActionFlags;
    private Vector2 localMoveInputVector;
    private Vector2 aimWorldPosition;
    private int smoothedFacingDirection;
    private bool hasSmoothedFacingDirection;

    private ushort localHealth;
    private ushort localBuilder;
    private ushort localSpender;
    private ushort localCurrency;

    private byte lastSeenCastSlot;
    private byte lastSeenCastResult;
    private byte lastSeenCastTargetTeam;
    private byte lastSeenCastAffected;
    private ushort lastSeenCastVfx;
    private byte lastSeenCastFeedbackTicks;

    private bool pickupPressedBuffered;
    private int fastAttackTapTicksRemaining;
    private int pickupIntentTicksRemaining;
    private int suppressFastAttackTicks;
    private bool interactPressedBufferedForSim;
    private bool interactPressedBufferedForZone;
    private bool returnHomePressedBuffered;
    private bool showLootNames;

    private KeyboardState previousKeyboard;
    private MouseState previousMouse;

    private UiScreen screen = UiScreen.Login;
    private bool pauseMenuOpen;
    private bool settingsReturnToInGame;
    private bool showVerboseHud;
    private string statusText = "Log in to continue.";
    private string activeTextField = string.Empty;

    private string loginUsername;
    private string loginPassword = string.Empty;
    private string hostField;
    private string portField;
    private string characterNameField;
    private float worldZoom = DefaultWorldZoom;
    private bool linearWorldFiltering;

    public ArmamentGame()
    {
        graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        config = ClientConfig.Load();
        slotStore = CharacterSlotStore.Load();
        netClient = new UdpProtocolClient();
        var derived = CharacterMath.ComputeDerived(CharacterAttributes.Default, CharacterStatTuning.Default);
        moveSpeedMilliPerSecond = derived.MoveSpeedMilliPerSecond;
        repoRoot = ResolveRepoRoot();

        loginUsername = config.AccountDisplayName;
        hostField = config.Host;
        portField = config.Port.ToString();
        characterNameField = $"Character {config.SelectedSlot + 1}";
        worldZoom = Math.Clamp(config.WorldZoom, 70f, 180f);
        linearWorldFiltering = false;
    }

    protected override void Initialize()
    {
        graphics.PreferredBackBufferWidth = 1440;
        graphics.PreferredBackBufferHeight = 900;
        graphics.ApplyChanges();
        Window.TextInput += OnTextInput;

        EnsureSelectedSlotLoaded();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);
        debugFont = Content.Load<SpriteFont>("DebugFont");
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
        screen = UiScreen.Login;
    }

    protected override void Update(GameTime gameTime)
    {
        if (spriteBatch is null || pixel is null || debugFont is null)
        {
            return;
        }

        totalGameSeconds = gameTime.TotalGameTime.TotalSeconds;
        frameDeltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;

        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();

        if (WasPressed(keyboard, previousKeyboard, Keys.Escape))
        {
            if (screen == UiScreen.InGame && joined)
            {
                pauseMenuOpen = !pauseMenuOpen;
            }
            else if (screen == UiScreen.Settings)
            {
                if (settingsReturnToInGame && joined)
                {
                    screen = UiScreen.InGame;
                    pauseMenuOpen = true;
                }
                else
                {
                    screen = UiScreen.CharacterSelect;
                }
            }
        }

        if (screen == UiScreen.InGame && WasPressed(keyboard, previousKeyboard, Keys.F10))
        {
            showVerboseHud = !showVerboseHud;
        }

        DrainIncoming();

        switch (screen)
        {
            case UiScreen.Login:
                UpdateLoginUi(mouse, keyboard);
                break;
            case UiScreen.CharacterSelect:
                UpdateCharacterSelectUi(mouse, keyboard);
                break;
            case UiScreen.CharacterCreation:
                UpdateCharacterCreateUi(mouse, keyboard);
                break;
            case UiScreen.Settings:
                UpdateSettingsUi(mouse, keyboard);
                break;
            case UiScreen.InGame:
                UpdateInGameUi(mouse, keyboard);
                if (!pauseMenuOpen)
                {
                    HandleInGameInputAndSimulation(keyboard, mouse, gameTime);
                }
                break;
        }

        previousKeyboard = keyboard;
        previousMouse = mouse;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (spriteBatch is null || pixel is null || debugFont is null)
        {
            return;
        }

        GraphicsDevice.Clear(new Color(41, 72, 125));
        textFields.Clear();

        switch (screen)
        {
            case UiScreen.Login:
                spriteBatch.Begin(samplerState: SamplerState.PointClamp);
                DrawLoginUi(spriteBatch, debugFont);
                spriteBatch.End();
                break;
            case UiScreen.CharacterSelect:
                spriteBatch.Begin(samplerState: SamplerState.PointClamp);
                DrawCharacterSelectUi(spriteBatch, debugFont);
                spriteBatch.End();
                break;
            case UiScreen.CharacterCreation:
                spriteBatch.Begin(samplerState: SamplerState.PointClamp);
                DrawCharacterCreateUi(spriteBatch, debugFont);
                spriteBatch.End();
                break;
            case UiScreen.Settings:
                spriteBatch.Begin(samplerState: SamplerState.PointClamp);
                DrawSettingsUi(spriteBatch, debugFont);
                spriteBatch.End();
                break;
            case UiScreen.InGame:
                // World pass: linear filtering improves perceived sprite quality when scaled.
                spriteBatch.Begin(samplerState: linearWorldFiltering ? SamplerState.LinearClamp : SamplerState.PointClamp);
                DrawWorld(spriteBatch, pixel, GetCameraPosition());
                spriteBatch.End();

                // UI pass: keep crisp for debug text and controls.
                spriteBatch.Begin(samplerState: SamplerState.PointClamp);
                DrawInGameHud(spriteBatch, debugFont);
                if (pauseMenuOpen)
                {
                    DrawPauseMenu(spriteBatch, debugFont);
                }
                spriteBatch.End();
                break;
        }

        base.Draw(gameTime);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        Window.TextInput -= OnTextInput;
        DisposeAnimationRuntime();
        netClient.Dispose();
        config.Save();
        slotStore.Save();
        base.OnExiting(sender, args);
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(activeTextField))
        {
            return;
        }

        if (e.Key == Keys.Back)
        {
            RemoveLastChar(activeTextField);
            return;
        }

        if (e.Character == '\t' || e.Character == '\r' || e.Character == '\n' || char.IsControl(e.Character))
        {
            return;
        }

        AppendChar(activeTextField, e.Character);
    }

    private void AppendChar(string field, char ch)
    {
        switch (field)
        {
            case "login_username": loginUsername += ch; break;
            case "login_password": loginPassword += ch; break;
            case "host": hostField += ch; break;
            case "port": if (char.IsDigit(ch)) portField += ch; break;
            case "char_name": characterNameField += ch; break;
        }
    }

    private void RemoveLastChar(string field)
    {
        static string TrimOne(string input) => input.Length > 0 ? input[..^1] : input;
        switch (field)
        {
            case "login_username": loginUsername = TrimOne(loginUsername); break;
            case "login_password": loginPassword = TrimOne(loginPassword); break;
            case "host": hostField = TrimOne(hostField); break;
            case "port": portField = TrimOne(portField); break;
            case "char_name": characterNameField = TrimOne(characterNameField); break;
        }
    }

    private void HandleInGameInputAndSimulation(KeyboardState keyboard, MouseState mouse, GameTime gameTime)
    {
        if (!joined)
        {
            return;
        }

        HandleInGameBufferInputs(keyboard, mouse);

        localTickAccumulator += (float)gameTime.ElapsedGameTime.TotalSeconds;
        var fixedDelta = 1f / SimulationHz;
        while (localTickAccumulator >= fixedDelta)
        {
            localTickAccumulator -= fixedDelta;
            RunLocalPredictionTick(keyboard, mouse);
        }

        BuildInterpolatedRenderState((float)totalGameSeconds);
    }

    private void HandleInGameBufferInputs(KeyboardState keyboard, MouseState mouse)
    {
        if (WasPressed(keyboard, previousKeyboard, Keys.LeftAlt) || WasPressed(keyboard, previousKeyboard, Keys.RightAlt))
        {
            showLootNames = !showLootNames;
        }

        if (WasPressed(keyboard, previousKeyboard, Keys.F))
        {
            interactPressedBufferedForZone = true;
            interactPressedBufferedForSim = true;
        }

        if (WasPressed(keyboard, previousKeyboard, Keys.H))
        {
            returnHomePressedBuffered = true;
        }

        if (WasPressed(keyboard, previousKeyboard, Keys.Z))
        {
            pickupPressedBuffered = true;
        }

        var lmbPressed = WasPressed(mouse, previousMouse, true);
        var clickedLoot = lmbPressed && IsLootClicked(mouse);
        if (clickedLoot)
        {
            pickupPressedBuffered = true;
            suppressFastAttackTicks = 10;
            LogCombat("Loot pickup");
        }
        else if (lmbPressed)
        {
            // Buffer quick taps so click-to-attack survives frame/tick timing differences.
            fastAttackTapTicksRemaining = Math.Max(fastAttackTapTicksRemaining, 3);
        }

        if (currentZone == ZoneKind.Overworld && interactPressedBufferedForZone)
        {
            interactPressedBufferedForZone = false;
        }
        else if (currentZone == ZoneKind.Dungeon && returnHomePressedBuffered)
        {
            returnHomePressedBuffered = false;
            netClient.Send(BuildJoinRequest(config));
        }
        else
        {
            interactPressedBufferedForZone = false;
            returnHomePressedBuffered = false;
        }

        aimWorldPosition = ScreenToWorld(new Vector2(mouse.X, mouse.Y), GetCameraPosition());
    }

    private bool IsLootClicked(MouseState mouse)
    {
        var clickWorld = ScreenToWorld(new Vector2(mouse.X, mouse.Y), GetCameraPosition());
        const float clickRadius = 0.9f;
        var radiusSq = clickRadius * clickRadius;
        foreach (var pair in latestEntityKinds)
        {
            if (pair.Value != EntityKind.Loot || pair.Key == PortalEntityId)
            {
                continue;
            }

            if (!latestEntities.TryGetValue(pair.Key, out var lootPos))
            {
                continue;
            }

            if ((lootPos - clickWorld).LengthSquared() <= radiusSq)
            {
                return true;
            }
        }

        return false;
    }

    private void RunLocalPredictionTick(KeyboardState keyboard, MouseState mouse)
    {
        if (localHealth == 0)
        {
            localActionFlags = InputActionFlags.None;
            previousActionFlags = InputActionFlags.None;
            localMoveInputVector = Vector2.Zero;
            return;
        }

        var movement = ReadMovementInput(keyboard);
        localMoveInputVector = movement;

        var actionFlags = ReadActionFlags(keyboard, mouse);
        if (fastAttackTapTicksRemaining > 0)
        {
            actionFlags |= InputActionFlags.FastAttackHold;
            fastAttackTapTicksRemaining--;
        }

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

        if (suppressFastAttackTicks > 0)
        {
            actionFlags &= ~InputActionFlags.FastAttackHold;
            suppressFastAttackTicks--;
        }

        RecordActionStarts(actionFlags);
        var started = actionFlags & ~previousActionFlags;
        if ((started & InputActionFlags.FastAttackHold) != 0)
        {
            localAnimator?.NotifyLocalFastAttackIntent();
        }

        var quantizedX = Quantization.QuantizeInput(movement.X);
        var quantizedY = Quantization.QuantizeInput(movement.Y);

        localClientTick++;
        var sequence = ++inputSequence;
        pendingInputs.Add(new PendingInput
        {
            Sequence = sequence,
            MoveX = quantizedX,
            MoveY = quantizedY
        });

        hasLocalPredictionState = true;
        IntegrateLocal(quantizedX, quantizedY);
        renderEntities[localEntityId] = MilliToVector(predictedLocalXMilli, predictedLocalYMilli);

        netClient.Send(new InputCommand
        {
            Sequence = sequence,
            ClientTick = localClientTick,
            MoveX = quantizedX,
            MoveY = quantizedY,
            ActionFlags = actionFlags
        });

        localActionFlags = actionFlags;
        previousActionFlags = actionFlags;
    }

    private void DrainIncoming()
    {
        while (netClient.TryDequeue(out var message))
        {
            switch (message)
            {
                case ServerHello:
                    netClient.Send(BuildJoinRequest(config));
                    LogCast("Connected: server hello");
                    break;
                case JoinOverworldAccepted accepted:
                    joined = true;
                    localEntityId = accepted.EntityId;
                    currentZone = accepted.ZoneKind;
                    currentInstanceId = 0;
                    ApplyAcceptedClassSpec(accepted.BaseClassId, accepted.SpecId);
                    screen = UiScreen.InGame;
                    pauseMenuOpen = false;
                    LogCast($"Joined Overworld as entity {localEntityId}");
                    break;
                case JoinDungeonAccepted accepted:
                    joined = true;
                    localEntityId = accepted.EntityId;
                    currentZone = accepted.ZoneKind;
                    currentInstanceId = accepted.DungeonInstanceId;
                    ApplyAcceptedClassSpec(accepted.BaseClassId, accepted.SpecId);
                    screen = UiScreen.InGame;
                    pauseMenuOpen = false;
                    LogCast($"Joined Dungeon instance {currentInstanceId} as entity {localEntityId}");
                    break;
                case WorldSnapshot snapshot:
                    ApplySnapshot(snapshot);
                    break;
            }
        }
    }

    private void ApplyAcceptedClassSpec(string baseClassId, string specId)
    {
        config.BaseClassId = ClassSpecCatalog.NormalizeBaseClass(baseClassId);
        config.SpecId = ClassSpecCatalog.NormalizeSpecForClass(config.BaseClassId, specId);

        CharacterSlotRecord slot;
        if (!slotStore.TryLoadSlot(config.AccountSubject, config.SelectedSlot, out var existing))
        {
            slot = new CharacterSlotRecord
            {
                Name = string.IsNullOrWhiteSpace(characterNameField) ? $"Character {config.SelectedSlot + 1}" : characterNameField,
                BaseClassId = config.BaseClassId,
                SpecId = config.SpecId
            };
        }
        else
        {
            slot = existing!;
            slot.BaseClassId = config.BaseClassId;
            slot.SpecId = config.SpecId;
        }

        slotStore.SaveSlot(config.AccountSubject, config.SelectedSlot, slot);
        slotStore.Save();
        config.Save();
        TryLoadAnimationRuntimeForLocalClass(config.BaseClassId, config.SpecId);
    }

    private void ApplySnapshot(WorldSnapshot snapshot)
    {
        latestEntities.Clear();
        latestEntityKinds.Clear();
        latestEntityHealth.Clear();
        latestEntityBuilderResource.Clear();
        latestEntitySpenderResource.Clear();
        latestLinkOwnerByEntity.Clear();
        latestLinkTargetByEntity.Clear();
        latestLinkRemainingTicks.Clear();
        latestEnemyAggroTarget.Clear();
        latestEnemyAggroThreat.Clear();
        latestEnemyForcedTicks.Clear();
        latestEnemyPrimaryStatusStacks.Clear();
        lootCurrencyById.Clear();

        currentZone = snapshot.ZoneKind;
        currentInstanceId = snapshot.InstanceId;

        foreach (var entity in snapshot.Entities)
        {
            var position = new Vector2(
                Quantization.DequantizePosition(entity.QuantizedX),
                Quantization.DequantizePosition(entity.QuantizedY));
            latestEntities[entity.EntityId] = position;
            latestEntityKinds[entity.EntityId] = entity.Kind;
            latestEntityHealth[entity.EntityId] = entity.Health;
            latestEntityBuilderResource[entity.EntityId] = entity.BuilderResource;
            latestEntitySpenderResource[entity.EntityId] = entity.SpenderResource;

            if (entity.Kind == EntityKind.Link)
            {
                latestLinkOwnerByEntity[entity.EntityId] = entity.BuilderResource;
                latestLinkTargetByEntity[entity.EntityId] = entity.SpenderResource;
                latestLinkRemainingTicks[entity.EntityId] = entity.Health;
            }

            if (entity.Kind == EntityKind.Enemy)
            {
                latestEnemyAggroTarget[entity.EntityId] = entity.AggroTargetEntityId;
                latestEnemyAggroThreat[entity.EntityId] = entity.AggroThreatValue;
                latestEnemyForcedTicks[entity.EntityId] = entity.ForcedTargetTicks;
                latestEnemyPrimaryStatusStacks[entity.EntityId] = entity.DebugPrimaryStatusStacks;
            }

            if (entity.Kind == EntityKind.Loot)
            {
                lootCurrencyById[entity.EntityId] = entity.Currency;
            }

            if (entity.EntityId == localEntityId)
            {
                localHealth = entity.Health;
                localBuilder = entity.BuilderResource;
                localSpender = entity.SpenderResource;
                localCurrency = entity.Currency;
                UpdateCooldownSlot(0, entity.FastCooldownTicks);
                UpdateCooldownSlot(1, entity.HeavyCooldownTicks);
                UpdateCooldownSlot(2, entity.Skill1CooldownTicks);
                UpdateCooldownSlot(3, entity.Skill2CooldownTicks);
                UpdateCooldownSlot(4, entity.Skill3CooldownTicks);
                UpdateCooldownSlot(5, entity.Skill4CooldownTicks);
                UpdateCooldownSlot(6, entity.Skill5CooldownTicks);
                UpdateCooldownSlot(7, entity.Skill6CooldownTicks);
                UpdateCooldownSlot(8, entity.Skill7CooldownTicks);
                UpdateCooldownSlot(9, entity.Skill8CooldownTicks);

                if (entity.DebugLastCastResultCode > 0 &&
                    (entity.DebugLastCastSlotCode != lastSeenCastSlot ||
                     entity.DebugLastCastResultCode != lastSeenCastResult ||
                     entity.DebugLastCastTargetTeamCode != lastSeenCastTargetTeam ||
                     entity.DebugLastCastAffectedCount != lastSeenCastAffected ||
                     entity.DebugLastCastVfxCode != lastSeenCastVfx ||
                     entity.DebugLastCastFeedbackTicks > lastSeenCastFeedbackTicks))
                {
                    localAnimator?.NotifyAuthoritativeCast(entity.DebugLastCastSlotCode, entity.DebugLastCastResultCode);

                    if (entity.DebugLastCastSlotCode != 0)
                    {
                        LogCast(FormatCastFeedback(entity.DebugLastCastSlotCode, entity.DebugLastCastResultCode));
                    }

                    if (entity.DebugConsumedStatusStacks > 0)
                    {
                        LogCast($"Burst payoff: consumed {entity.DebugConsumedStatusStacks} stack(s)");
                    }
                }

                lastSeenCastSlot = entity.DebugLastCastSlotCode;
                lastSeenCastResult = entity.DebugLastCastResultCode;
                lastSeenCastTargetTeam = entity.DebugLastCastTargetTeamCode;
                lastSeenCastAffected = entity.DebugLastCastAffectedCount;
                lastSeenCastVfx = entity.DebugLastCastVfxCode;
                lastSeenCastFeedbackTicks = entity.DebugLastCastFeedbackTicks;
            }
        }

        if (latestEntities.TryGetValue(localEntityId, out var authoritativeLocal))
        {
            predictedLocalXMilli = (int)MathF.Round(authoritativeLocal.X * 1000f);
            predictedLocalYMilli = (int)MathF.Round(authoritativeLocal.Y * 1000f);
            hasLocalPredictionState = true;

            if (snapshot.LastProcessedInputSequence > 0)
            {
                pendingInputs.RemoveAll(p => p.Sequence <= snapshot.LastProcessedInputSequence);
            }

            for (var i = 0; i < pendingInputs.Count; i++)
            {
                IntegrateLocal(pendingInputs[i].MoveX, pendingInputs[i].MoveY);
            }

            renderEntities[localEntityId] = MilliToVector(predictedLocalXMilli, predictedLocalYMilli);
        }

        var receivedAt = (float)totalGameSeconds;
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
                ReceivedAtSeconds = receivedAt,
                Position = latestEntities[entity.EntityId]
            });

            if (samples.Count > 32)
            {
                samples.RemoveRange(0, samples.Count - 32);
            }
        }

        var removed = remoteEntitySamples.Keys.Where(id => !latestEntityKinds.ContainsKey(id)).ToArray();
        foreach (var id in removed)
        {
            remoteEntitySamples.Remove(id);
            renderEntities.Remove(id);
        }

        hasSnapshot = true;
    }

    private void BuildInterpolatedRenderState(float nowSeconds)
    {
        var targetTime = nowSeconds - InterpolationBackTimeSeconds;

        foreach (var pair in remoteEntitySamples)
        {
            var entityId = pair.Key;
            var samples = pair.Value;
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
                var span = MathF.Max(0.0001f, newer.ReceivedAtSeconds - older.ReceivedAtSeconds);
                var t = MathHelper.Clamp((targetTime - older.ReceivedAtSeconds) / span, 0f, 1f);
                renderEntities[entityId] = Vector2.Lerp(older.Position, newer.Position, t);
            }
            else
            {
                renderEntities[entityId] = samples[0].Position;
            }
        }

        foreach (var pair in latestEntityKinds)
        {
            if ((pair.Value == EntityKind.Loot || pair.Value == EntityKind.Zone || pair.Value == EntityKind.Link) &&
                latestEntities.TryGetValue(pair.Key, out var pos))
            {
                renderEntities[pair.Key] = pos;
            }
        }
    }

    private void DrawWorld(SpriteBatch batch, Texture2D px, Vector2 cameraPos)
    {
        foreach (var pair in renderEntities)
        {
            if (!latestEntityKinds.TryGetValue(pair.Key, out var kind))
            {
                continue;
            }

            var worldPos = pair.Value;
            var screenPos = WorldToScreen(worldPos, cameraPos);
            if (kind == EntityKind.Player && pair.Key == localEntityId && TryDrawLocalAnimatedPlayer(batch, screenPos, worldPos))
            {
                continue;
            }

            var color = ResolveColor(pair.Key, kind);
            var size = ResolveSize(kind);
            var rect = new Rectangle((int)(screenPos.X - size * 0.5f), (int)(screenPos.Y - size * 0.5f), (int)size, (int)size);
            batch.Draw(px, rect, color);

            if (kind == EntityKind.Zone)
            {
                DrawOutline(batch, rect, Color.LightBlue);
            }

            if (kind == EntityKind.Link && latestLinkOwnerByEntity.TryGetValue(pair.Key, out var ownerId) && latestLinkTargetByEntity.TryGetValue(pair.Key, out var targetId))
            {
                DrawLinkLine(batch, px, cameraPos, ownerId, targetId);
            }

            if (kind == EntityKind.Loot && showLootNames && pair.Key != PortalEntityId && debugFont is not null)
            {
                var amount = lootCurrencyById.TryGetValue(pair.Key, out var currency) ? currency : (ushort)0;
                batch.DrawString(debugFont, amount > 0 ? $"Gold {amount}" : "Loot", screenPos + new Vector2(-22f, -30f), Color.White);
            }
        }

        DrawPortalPrompt(batch, cameraPos);
    }

    private bool TryDrawLocalAnimatedPlayer(SpriteBatch batch, Vector2 screenPos, Vector2 worldPos)
    {
        if (localAnimationRuntime is null || localAnimator is null)
        {
            return false;
        }

        var moving = localMoveInputVector.LengthSquared() > 0.0001f;
        var blockHold = (localActionFlags & InputActionFlags.BlockHold) != 0;
        var fastHold = (localActionFlags & InputActionFlags.FastAttackHold) != 0;
        var heavyHold = (localActionFlags & InputActionFlags.HeavyAttackHold) != 0;
        var facingDir = ComputeFacingDirection(worldPos);
        if (!localAnimator.TryResolveFrame(frameDeltaSeconds, facingDir, localMoveInputVector, moving, blockHold, fastHold, heavyHold, out var clip, out var frame))
        {
            return false;
        }

        var scale = worldZoom / MathF.Max(1, clip.PixelsPerUnit);
        var snappedScreenPos = new Vector2(MathF.Round(screenPos.X), MathF.Round(screenPos.Y));
        batch.Draw(
            clip.Atlas,
            snappedScreenPos,
            frame.Source,
            Color.White,
            0f,
            frame.OriginPixels,
            scale,
            SpriteEffects.None,
            0f);

        return true;
    }

    private int ComputeFacingDirection(Vector2 worldPos)
    {
        var activeCombatOrMove = localMoveInputVector.LengthSquared() > 0.0001f ||
                                 (localActionFlags & (InputActionFlags.FastAttackHold | InputActionFlags.HeavyAttackHold | InputActionFlags.BlockHold)) != 0;

        Vector2 dir;
        if (activeCombatOrMove && aimWorldPosition != Vector2.Zero)
        {
            dir = aimWorldPosition - worldPos;
        }
        else if (localMoveInputVector.LengthSquared() > 0.0001f)
        {
            dir = localMoveInputVector;
        }
        else
        {
            return hasSmoothedFacingDirection ? smoothedFacingDirection : 7;
        }

        if (dir.LengthSquared() <= 0.0001f)
        {
            return hasSmoothedFacingDirection ? smoothedFacingDirection : 7;
        }

        // Atlas direction convention (clockwise):
        // 0=NE, 1=E, 2=SE, 3=S, 4=SW, 5=W, 6=NW, 7=N
        var angleDeg = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);
        var raw = (45f - angleDeg) / 45f;
        var index = (int)MathF.Round(raw);
        index %= 8;
        if (index < 0)
        {
            index += 8;
        }

        if (!hasSmoothedFacingDirection)
        {
            smoothedFacingDirection = index;
            hasSmoothedFacingDirection = true;
            return smoothedFacingDirection;
        }

        var hysteresisDegrees = activeCombatOrMove ? 20f : 30f;
        var currentCenterAngle = 45f - smoothedFacingDirection * 45f;
        var deltaDegrees = ShortestAngleDegrees(currentCenterAngle, angleDeg);
        if (MathF.Abs(deltaDegrees) >= hysteresisDegrees)
        {
            smoothedFacingDirection = index;
        }

        return smoothedFacingDirection;
    }

    private static float ShortestAngleDegrees(float fromDeg, float toDeg)
    {
        var delta = (toDeg - fromDeg + 540f) % 360f - 180f;
        return delta;
    }

    private void TryLoadAnimationRuntimeForLocalClass(string baseClassId, string specId)
    {
        var normalizedClass = ClassSpecCatalog.NormalizeBaseClass(baseClassId);
        var normalizedSpec = ClassSpecCatalog.NormalizeSpecForClass(normalizedClass, specId);
        if (string.Equals(loadedAnimationClassId, normalizedClass, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(loadedAnimationSpecId, normalizedSpec, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DisposeAnimationRuntime();
        localAnimationRuntime = AtlasAnimationLoader.TryLoad(GraphicsDevice, repoRoot, normalizedClass, normalizedSpec);
        if (localAnimationRuntime is not null)
        {
            localAnimator = new LocalAtlasAnimator(localAnimationRuntime);
            loadedAnimationClassId = normalizedClass;
            loadedAnimationSpecId = normalizedSpec;
            LogCast($"Animation runtime loaded for {normalizedClass}/{normalizedSpec}");
            return;
        }

        loadedAnimationClassId = normalizedClass;
        loadedAnimationSpecId = normalizedSpec;
        localAnimator = null;
        LogCast($"Animation runtime missing for {normalizedClass}/{normalizedSpec} (using debug fallback)");
    }

    private void DisposeAnimationRuntime()
    {
        localAnimationRuntime?.Dispose();
        localAnimationRuntime = null;
        localAnimator = null;
        loadedAnimationClassId = string.Empty;
        loadedAnimationSpecId = string.Empty;
    }

    private static string ResolveRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "content")) &&
                Directory.Exists(Path.Combine(dir, "shared-sim")) &&
                Directory.Exists(Path.Combine(dir, "server-dotnet")))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private void DrawPortalPrompt(SpriteBatch batch, Vector2 cameraPos)
    {
        if (currentZone != ZoneKind.Overworld || !joined || debugFont is null)
        {
            return;
        }

        if (!latestEntities.TryGetValue(PortalEntityId, out var portalPos) ||
            !latestEntities.TryGetValue(localEntityId, out var localPos))
        {
            return;
        }

        var inRange = (portalPos - localPos).LengthSquared() <= 4.5f;
        if (!inRange)
        {
            return;
        }

        var p = WorldToScreen(portalPos, cameraPos);
        batch.DrawString(debugFont, "[F] Dungeon", p + new Vector2(-36f, -42f), Color.White);
    }

    private void DrawInGameHud(SpriteBatch batch, SpriteFont font)
    {
        var viewW = graphics.PreferredBackBufferWidth;
        var viewH = graphics.PreferredBackBufferHeight;
        var dock = new Rectangle(10, viewH - 122, viewW - 20, 112);
        batch.Draw(pixel!, dock, new Color(9, 16, 30, 218));
        DrawOutline(batch, dock, new Color(150, 168, 208, 220));

        var bookendW = 190;
        var hpPanel = new Rectangle(dock.X + 12, dock.Y + 10, bookendW, dock.Height - 20);
        DrawGaugePanel(batch, font, hpPanel, "HEALTH", localHealth, CharacterMath.ComputeDerived(CharacterAttributes.Default, CharacterStatTuning.Default).MaxHealth, new Color(180, 35, 35, 255), $"{localHealth}");

        var resourcePanel = new Rectangle(dock.Right - 12 - bookendW, dock.Y + 10, bookendW, dock.Height - 20);
        DrawGaugePanel(batch, font, resourcePanel, "RESOURCE", localSpender, 100, new Color(210, 140, 35, 255), $"{localSpender}");

        var centerLeft = hpPanel.Right + 12;
        var centerRight = resourcePanel.Left - 12;
        var centerWidth = Math.Max(360, centerRight - centerLeft);

        var staminaCenterPanel = new Rectangle(centerLeft + 60, dock.Y + 8, Math.Max(220, centerWidth - 120), 26);
        DrawHorizontalBar(batch, font, staminaCenterPanel, "Stamina", localBuilder, 100, new Color(60, 150, 240, 255));

        var slotPanel = new Rectangle(centerLeft, staminaCenterPanel.Bottom + 8, centerWidth, 60);
        batch.Draw(pixel!, slotPanel, new Color(16, 25, 45, 220));
        DrawOutline(batch, slotPanel, new Color(155, 170, 205, 220));
        DrawActionBar(batch, font, slotPanel);

        var status = $"[{currentZone}] {config.BaseClassId}/{config.SpecId} | Gold {localCurrency} | Aggro {(ResolveAggroSummary())}";
        batch.DrawString(font, status, new Vector2(dock.X + 2, dock.Y - 20), new Color(225, 230, 245, 235));
        batch.DrawString(font, "F10 debug", new Vector2(dock.Right - 100, dock.Y - 20), new Color(210, 220, 240, 220));

        if (showVerboseHud)
        {
            var top = new Rectangle(8, 8, graphics.PreferredBackBufferWidth - 16, 132);
            batch.Draw(pixel!, top, new Color(11, 32, 64, 210));
            DrawOutline(batch, top, Color.White);
            var lines = new[]
            {
                "Armament MonoGame Client",
                $"Connection: {(netClient.IsConnected ? "connected" : "disconnected")} | Joined: {(joined ? "yes" : "no")} | Zone: {currentZone} ({currentInstanceId})",
                $"Server: {config.Host}:{config.Port} | Account: {config.AccountSubject} ({config.AccountDisplayName})",
                $"Local Entity: {localEntityId} | Snapshot: {(hasSnapshot ? "yes" : "no")} | Prediction: {(hasLocalPredictionState ? "yes" : "no")} | Links {latestEntityKinds.Values.Count(k => k == EntityKind.Link)}",
                $"Move: {localMoveInputVector.X:0.00},{localMoveInputVector.Y:0.00} | Aim: {aimWorldPosition.X:0.00},{aimWorldPosition.Y:0.00}",
                "Controls: WASD | LMB/RMB/Shift | E/R/Q/T + 1/2/3/4 | Z/F/H | Alt names | Esc menu"
            };
            var y = 14f;
            for (var i = 0; i < lines.Length; i++)
            {
                batch.DrawString(font, lines[i], new Vector2(16f, y), Color.White);
                y += 19f;
            }

            DrawFeedPanel(batch, font, "Combat", combatFeed, 8, 146, 460, 152);
            DrawFeedPanel(batch, font, "Server Cast Feed", castFeed, 476, 146, 460, 152);
        }
    }

    private void DrawGaugePanel(SpriteBatch batch, SpriteFont font, Rectangle rect, string title, int value, int max, Color fill, string valueLabel)
    {
        batch.Draw(pixel!, rect, new Color(14, 30, 55, 220));
        DrawOutline(batch, rect, new Color(155, 170, 205, 220));
        batch.DrawString(font, title, new Vector2(rect.X + 8, rect.Y + 6), Color.White);
        var barRect = new Rectangle(rect.X + 8, rect.Y + 30, rect.Width - 16, rect.Height - 38);
        DrawMeterFill(batch, barRect, value, max, fill);
        batch.DrawString(font, valueLabel, new Vector2(barRect.X + 8, barRect.Y + 6), Color.White);
    }

    private void DrawHorizontalBar(SpriteBatch batch, SpriteFont font, Rectangle rect, string label, int value, int max, Color fill)
    {
        batch.Draw(pixel!, rect, new Color(20, 26, 36, 230));
        DrawOutline(batch, rect, new Color(120, 130, 160, 255));
        DrawMeterFill(batch, rect, value, max, fill);
        batch.DrawString(font, $"{label}: {value}", new Vector2(rect.X + 6, rect.Y + 2), Color.White);
    }

    private void DrawMeterFill(SpriteBatch batch, Rectangle rect, int value, int max, Color fill)
    {
        var clampedMax = Math.Max(1, max);
        var t = MathHelper.Clamp(value / (float)clampedMax, 0f, 1f);
        var fillWidth = Math.Max(1, (int)MathF.Round((rect.Width - 2) * t));
        var fillRect = new Rectangle(rect.X + 1, rect.Y + 1, fillWidth, Math.Max(1, rect.Height - 2));
        batch.Draw(pixel!, fillRect, fill);
    }

    private void DrawActionBar(SpriteBatch batch, SpriteFont font, Rectangle panel)
    {
        // Slot order adheres to current keybind contract.
        var labels = new[] { "LMB", "RMB", "Shift", "E", "R", "Q", "T", "1", "2", "3", "4" };
        var cooldowns = new[] { localCooldownTicks[0], localCooldownTicks[1], (ushort)0, localCooldownTicks[2], localCooldownTicks[3], localCooldownTicks[4], localCooldownTicks[5], localCooldownTicks[6], localCooldownTicks[7], localCooldownTicks[8], localCooldownTicks[9] };
        var cooldownStarts = new[] { localCooldownStartTicks[0], localCooldownStartTicks[1], (ushort)0, localCooldownStartTicks[2], localCooldownStartTicks[3], localCooldownStartTicks[4], localCooldownStartTicks[5], localCooldownStartTicks[6], localCooldownStartTicks[7], localCooldownStartTicks[8], localCooldownStartTicks[9] };
        var cols = labels.Length;
        var spacing = 6;
        var slotW = (panel.Width - (spacing * (cols + 1))) / cols;
        var slotH = panel.Height - 16;
        var y = panel.Y + 8;

        for (var i = 0; i < cols; i++)
        {
            var x = panel.X + spacing + i * (slotW + spacing);
            var r = new Rectangle(x, y, slotW, slotH);
            batch.Draw(pixel!, r, new Color(22, 34, 58, 240));
            DrawOutline(batch, r, new Color(130, 145, 180, 255));
            batch.DrawString(font, labels[i], new Vector2(r.X + 6, r.Y + 6), Color.White);

            if (cooldowns[i] > 0)
            {
                var baseline = Math.Max(1, (int)cooldownStarts[i]);
                var t = MathHelper.Clamp(cooldowns[i] / (float)baseline, 0f, 1f);
                var h = Math.Max(1, (int)MathF.Round((r.Height - 2) * t));
                var overlay = new Rectangle(r.X + 1, r.Bottom - 1 - h, r.Width - 2, h);
                batch.Draw(pixel!, overlay, new Color(8, 8, 10, 185));
                var sec = cooldowns[i] / 60f;
                var text = sec >= 10f ? $"{sec:0}" : $"{sec:0.0}";
                batch.DrawString(font, text, new Vector2(r.X + 6, r.Bottom - 24), new Color(235, 235, 245, 255));
            }
        }
    }

    private string ResolveAggroSummary()
    {
        foreach (var pair in latestEnemyAggroTarget)
        {
            if (pair.Value == localEntityId)
            {
                var threat = latestEnemyAggroThreat.TryGetValue(pair.Key, out var t) ? t : (ushort)0;
                var forced = latestEnemyForcedTicks.TryGetValue(pair.Key, out var f) ? f : (byte)0;
                return $"enemy {pair.Key}: threat={threat} forced={forced}";
            }
        }

        return "none";
    }

    private void UpdateCooldownSlot(int idx, ushort ticks)
    {
        if ((uint)idx >= (uint)localCooldownTicks.Length)
        {
            return;
        }

        var previous = localCooldownTicks[idx];
        localCooldownTicks[idx] = ticks;
        if (ticks == 0)
        {
            localCooldownStartTicks[idx] = 0;
            return;
        }

        // Record baseline when cooldown starts or jumps up.
        if (previous == 0 || ticks > localCooldownStartTicks[idx])
        {
            localCooldownStartTicks[idx] = ticks;
        }
    }

    private void DrawPauseMenu(SpriteBatch batch, SpriteFont font)
    {
        uiButtons.Clear();
        var rect = new Rectangle(graphics.PreferredBackBufferWidth / 2 - 220, graphics.PreferredBackBufferHeight / 2 - 170, 440, 340);
        batch.Draw(pixel!, rect, new Color(5, 18, 40, 240));
        DrawOutline(batch, rect, Color.White);
        batch.DrawString(font, "Pause Menu", new Vector2(rect.X + 150, rect.Y + 18), Color.White);

        AddButton(rect.X + 40, rect.Y + 60, 360, 34, "Resume", () => pauseMenuOpen = false);
        AddButton(rect.X + 40, rect.Y + 102, 360, 34, "Return To Character Select", ReturnToCharacterSelect);
        AddButton(rect.X + 40, rect.Y + 144, 360, 34, "Logout", LogoutToLogin);
        AddButton(rect.X + 40, rect.Y + 186, 360, 34, "Settings", () =>
        {
            pauseMenuOpen = false;
            settingsReturnToInGame = true;
            screen = UiScreen.Settings;
        });
        AddButton(rect.X + 40, rect.Y + 228, 360, 34, "Exit Game", Exit);

        DrawButtons(batch, font);
    }

    private void DrawFeedPanel(SpriteBatch batch, SpriteFont font, string title, List<string> entries, int x, int y, int w, int h)
    {
        batch.Draw(pixel!, new Rectangle(x, y, w, h), new Color(11, 32, 64, 220));
        DrawOutline(batch, new Rectangle(x, y, w, h), Color.White);
        batch.DrawString(font, title, new Vector2(x + 8, y + 8), Color.White);

        var lineY = y + 30;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            batch.DrawString(font, $"- {entries[i]}", new Vector2(x + 8, lineY), Color.White);
            lineY += 18;
            if (lineY > y + h - 22)
            {
                break;
            }
        }
    }

    private void DrawLoginUi(SpriteBatch batch, SpriteFont font)
    {
        uiButtons.Clear();
        var panel = new Rectangle(graphics.PreferredBackBufferWidth / 2 - 260, graphics.PreferredBackBufferHeight / 2 - 200, 520, 400);
        batch.Draw(pixel!, panel, new Color(10, 24, 48, 240));
        DrawOutline(batch, panel, Color.White);

        DrawCentered(batch, font, "ARMAMENT", panel.X, panel.Y + 18, panel.Width, Color.White);
        DrawCentered(batch, font, "LOGIN", panel.X, panel.Y + 48, panel.Width, Color.White);

        DrawTextField(batch, font, "USERNAME", "login_username", loginUsername, new Rectangle(panel.X + 42, panel.Y + 96, panel.Width - 84, 30), false);
        DrawTextField(batch, font, "PASSWORD", "login_password", loginPassword, new Rectangle(panel.X + 42, panel.Y + 150, panel.Width - 84, 30), true);

        AddButton(panel.X + 42, panel.Y + 198, panel.Width - 84, 34, "LOG IN", () => PerformLogin(false));
        AddButton(panel.X + 42, panel.Y + 240, panel.Width - 84, 30, "CREATE ACCOUNT", () => PerformLogin(true));

        batch.DrawString(font, statusText, new Vector2(panel.X + 42, panel.Y + 288), Color.White);
        DrawButtons(batch, font);
    }

    private void DrawCharacterSelectUi(SpriteBatch batch, SpriteFont font)
    {
        uiButtons.Clear();
        var panel = new Rectangle(graphics.PreferredBackBufferWidth / 2 - 420, graphics.PreferredBackBufferHeight / 2 - 260, 840, 520);
        batch.Draw(pixel!, panel, new Color(10, 24, 48, 240));
        DrawOutline(batch, panel, Color.White);
        DrawCentered(batch, font, "CHARACTER SELECT", panel.X, panel.Y + 14, panel.Width, Color.White);

        batch.DrawString(font, $"Account: {config.AccountDisplayName} ({config.AccountSubject})", new Vector2(panel.X + 22, panel.Y + 66), Color.White);

        var filledSlots = slotStore.GetFilledSlots(config.AccountSubject, MaxSlots);
        var nextEmpty = slotStore.GetNextEmptySlot(config.AccountSubject, MaxSlots);
        var row = 0;
        for (var i = 0; i < filledSlots.Count; i++)
        {
            var s = filledSlots[i];
            var selected = s == config.SelectedSlot;
            var rect = new Rectangle(panel.X + 22, panel.Y + 96 + row * 42, 300, 34);
            DrawSelectable(batch, font, rect, GetSlotSummary(s), selected, () => SelectSlot(s));
            row++;
        }

        if (nextEmpty >= 0)
        {
            var rect = new Rectangle(panel.X + 22, panel.Y + 96 + row * 42, 300, 34);
            AddButton(rect.X, rect.Y, rect.Width, rect.Height, $"Slot {nextEmpty}: + Create Character", () => StartCreate(nextEmpty));
            row++;
        }

        var detail = new Rectangle(panel.X + 350, panel.Y + 84, panel.Width - 372, 320);
        batch.Draw(pixel!, detail, new Color(7, 19, 40, 240));
        DrawOutline(batch, detail, Color.White);
        var hasSlot = slotStore.TryLoadSlot(config.AccountSubject, config.SelectedSlot, out var currentSlot);

        batch.DrawString(font, $"Slot {config.SelectedSlot}", new Vector2(detail.X + 12, detail.Y + 12), Color.White);
        batch.DrawString(font, $"Name: {(hasSlot ? currentSlot!.Name : "<empty>")}", new Vector2(detail.X + 12, detail.Y + 42), Color.White);
        batch.DrawString(font, $"Class: {(hasSlot ? currentSlot!.BaseClassId : "<none>")}", new Vector2(detail.X + 12, detail.Y + 68), Color.White);
        batch.DrawString(font, $"Spec: {(hasSlot ? currentSlot!.SpecId : "<none>")}", new Vector2(detail.X + 12, detail.Y + 94), Color.White);

        DrawTextField(batch, font, "SERVER HOST", "host", hostField, new Rectangle(detail.X + 12, detail.Y + 150, detail.Width - 24, 28), false);
        DrawTextField(batch, font, "SERVER PORT", "port", portField, new Rectangle(detail.X + 12, detail.Y + 204, detail.Width - 24, 28), false);

        AddButton(detail.X + 12, detail.Y + 246, detail.Width - 24, 34, hasSlot ? "PLAY" : "PLAY (CREATE CHARACTER FIRST)", ConnectFromSelectedSlot, hasSlot);

        var bottomY = panel.Bottom - 74;
        AddButton(panel.X + 22, bottomY, 160, 32, "DELETE", DeleteSelectedSlot, hasSlot);
        AddButton(detail.X, bottomY, 160, 32, "LOGOUT", LogoutToLogin);
        AddButton(detail.X + 172, bottomY, 160, 32, "SETTINGS", () =>
        {
            settingsReturnToInGame = false;
            screen = UiScreen.Settings;
        });

        batch.DrawString(font, hasSlot ? statusText : "Selected slot is empty. Create character first.", new Vector2(detail.X, panel.Bottom - 36), Color.White);

        DrawButtons(batch, font);
    }

    private void DrawCharacterCreateUi(SpriteBatch batch, SpriteFont font)
    {
        uiButtons.Clear();
        var panel = new Rectangle(graphics.PreferredBackBufferWidth / 2 - 420, graphics.PreferredBackBufferHeight / 2 - 260, 840, 520);
        batch.Draw(pixel!, panel, new Color(10, 24, 48, 240));
        DrawOutline(batch, panel, Color.White);
        DrawCentered(batch, font, "CHARACTER CREATION", panel.X, panel.Y + 14, panel.Width, Color.White);

        batch.DrawString(font, $"Slot: {config.SelectedSlot}", new Vector2(panel.X + 30, panel.Y + 80), Color.White);
        DrawTextField(batch, font, "CHARACTER NAME", "char_name", characterNameField, new Rectangle(panel.X + 30, panel.Y + 124, 320, 30), false);

        var classes = ClassSpecCatalog.BaseClasses;
        for (var i = 0; i < classes.Length; i++)
        {
            var row = i / 2;
            var col = i % 2;
            var rect = new Rectangle(panel.X + 30 + col * 164, panel.Y + 180 + row * 34, 156, 28);
            var classId = classes[i];
            var selected = string.Equals(config.BaseClassId, classId, StringComparison.OrdinalIgnoreCase);
            DrawSelectable(batch, font, rect, Capitalize(classId), selected, () =>
            {
                config.BaseClassId = classId;
                var specs = ClassSpecCatalog.GetSpecsForClass(config.BaseClassId);
                if (specs.Count > 0)
                {
                    config.SpecId = specs[0];
                }
            });
        }

        var specsList = ClassSpecCatalog.GetSpecsForClass(config.BaseClassId);
        batch.DrawString(font, "Spec", new Vector2(panel.X + 420, panel.Y + 102), Color.White);
        for (var i = 0; i < specsList.Count; i++)
        {
            var specId = specsList[i];
            var rect = new Rectangle(panel.X + 420, panel.Y + 126 + i * 34, 330, 28);
            var selected = string.Equals(config.SpecId, specId, StringComparison.OrdinalIgnoreCase);
            DrawSelectable(batch, font, rect, specId.Replace("spec.", string.Empty), selected, () => config.SpecId = specId);
        }

        AddButton(panel.X + 30, panel.Bottom - 72, 180, 34, "CANCEL", () =>
        {
            screen = UiScreen.CharacterSelect;
            EnsureSelectedSlotLoaded();
        });

        var nextEmpty = slotStore.GetNextEmptySlot(config.AccountSubject, MaxSlots);
        var canCreate = !string.IsNullOrWhiteSpace(characterNameField) && nextEmpty == config.SelectedSlot;

        AddButton(panel.X + 224, panel.Bottom - 72, 180, 34, "CREATE", () =>
        {
            var slot = new CharacterSlotRecord
            {
                Name = characterNameField.Trim(),
                BaseClassId = ClassSpecCatalog.NormalizeBaseClass(config.BaseClassId),
                SpecId = ClassSpecCatalog.NormalizeSpecForClass(config.BaseClassId, config.SpecId)
            };
            slotStore.SaveSlot(config.AccountSubject, config.SelectedSlot, slot);
            slotStore.Save();
            config.CharacterName = slot.Name;
            config.BaseClassId = slot.BaseClassId;
            config.SpecId = slot.SpecId;
            config.Save();
            statusText = $"Created slot {config.SelectedSlot}: {slot.Name}";
            screen = UiScreen.CharacterSelect;
        }, canCreate);

        if (!canCreate)
        {
            batch.DrawString(font, "Create is only allowed in the next empty slot.", new Vector2(panel.X + 420, panel.Bottom - 62), Color.White);
        }

        DrawButtons(batch, font);
    }

    private void DrawSettingsUi(SpriteBatch batch, SpriteFont font)
    {
        uiButtons.Clear();
        var panel = new Rectangle(graphics.PreferredBackBufferWidth / 2 - 280, graphics.PreferredBackBufferHeight / 2 - 180, 560, 360);
        batch.Draw(pixel!, panel, new Color(10, 24, 48, 240));
        DrawOutline(batch, panel, Color.White);
        DrawCentered(batch, font, "SETTINGS", panel.X, panel.Y + 16, panel.Width, Color.White);

        DrawTextField(batch, font, "NETWORK HOST", "host", hostField, new Rectangle(panel.X + 28, panel.Y + 90, panel.Width - 56, 28), false);
        DrawTextField(batch, font, "NETWORK PORT", "port", portField, new Rectangle(panel.X + 28, panel.Y + 150, panel.Width - 56, 28), false);

        batch.DrawString(font, $"WORLD ZOOM: {worldZoom:0}", new Vector2(panel.X + 28, panel.Y + 196), Color.White);
        AddButton(panel.X + 210, panel.Y + 192, 32, 28, "-", () => worldZoom = Math.Clamp(worldZoom - 4f, 70f, 180f));
        AddButton(panel.X + 246, panel.Y + 192, 32, 28, "+", () => worldZoom = Math.Clamp(worldZoom + 4f, 70f, 180f));

        var filterLabel = linearWorldFiltering ? "World Filter: Linear (Smooth)" : "World Filter: Nearest (No Filter)";
        AddButton(panel.X + 28, panel.Y + 232, 250, 30, filterLabel, () => linearWorldFiltering = !linearWorldFiltering);

        AddButton(panel.X + 28, panel.Bottom - 50, 180, 30, "SAVE", () =>
        {
            SaveConnectionFields();
            config.WorldZoom = worldZoom;
            config.LinearWorldFiltering = linearWorldFiltering;
            config.Save();
            statusText = "Settings saved.";
            if (settingsReturnToInGame && joined)
            {
                screen = UiScreen.InGame;
                pauseMenuOpen = true;
            }
            else
            {
                screen = UiScreen.CharacterSelect;
            }
        });

        AddButton(panel.Right - 208, panel.Bottom - 50, 180, 30, "BACK", () =>
        {
            if (settingsReturnToInGame && joined)
            {
                screen = UiScreen.InGame;
                pauseMenuOpen = true;
            }
            else
            {
                screen = UiScreen.CharacterSelect;
            }
        });

        DrawButtons(batch, font);
    }

    private void UpdateLoginUi(MouseState mouse, KeyboardState keyboard)
    {
        HandleUiMouse(mouse);
    }

    private void UpdateCharacterSelectUi(MouseState mouse, KeyboardState keyboard)
    {
        HandleUiMouse(mouse);
    }

    private void UpdateCharacterCreateUi(MouseState mouse, KeyboardState keyboard)
    {
        HandleUiMouse(mouse);
    }

    private void UpdateSettingsUi(MouseState mouse, KeyboardState keyboard)
    {
        HandleUiMouse(mouse);
    }

    private void UpdateInGameUi(MouseState mouse, KeyboardState keyboard)
    {
        if (pauseMenuOpen)
        {
            HandleUiMouse(mouse);
        }
    }

    private void HandleUiMouse(MouseState mouse)
    {
        if (!WasPressed(mouse, previousMouse, true))
        {
            return;
        }

        var point = new Point(mouse.X, mouse.Y);

        var focusedField = FindClickedTextField(point);
        activeTextField = focusedField ?? string.Empty;

        for (var i = 0; i < uiButtons.Count; i++)
        {
            var button = uiButtons[i];
            if (button.Enabled && button.Bounds.Contains(point))
            {
                button.OnClick();
                break;
            }
        }
    }

    private string? FindClickedTextField(Point point)
    {
        for (var i = 0; i < textFields.Count; i++)
        {
            if (textFields[i].Bounds.Contains(point))
            {
                return textFields[i].Id;
            }
        }

        return null;
    }

    private void PerformLogin(bool createAccount)
    {
        var trimmed = loginUsername.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            statusText = "Username is required.";
            return;
        }

        config.AccountDisplayName = trimmed;
        config.AccountSubject = $"local:{CharacterSlotStore.NormalizeSubject(trimmed)}";
        config.Save();

        EnsureSelectedSlotLoaded();
        screen = UiScreen.CharacterSelect;
        statusText = createAccount ? "Account created locally." : "Logged in.";
    }

    private void SelectSlot(int slot)
    {
        config.SelectedSlot = slot;
        EnsureSelectedSlotLoaded();
        config.Save();
    }

    private void StartCreate(int slot)
    {
        config.SelectedSlot = slot;
        config.BaseClassId = ClassSpecCatalog.NormalizeBaseClass(null);
        config.SpecId = ClassSpecCatalog.NormalizeSpecForClass(config.BaseClassId, null);
        characterNameField = GetDefaultNameForSlot(slot);
        activeTextField = "char_name";
        screen = UiScreen.CharacterCreation;
    }

    private void DeleteSelectedSlot()
    {
        slotStore.DeleteSlotAndCompact(config.AccountSubject, config.SelectedSlot, MaxSlots);
        slotStore.Save();

        var filled = slotStore.GetFilledSlots(config.AccountSubject, MaxSlots);
        if (filled.Count == 0)
        {
            config.SelectedSlot = 0;
            config.CharacterName = string.Empty;
            config.BaseClassId = ClassSpecCatalog.NormalizeBaseClass(null);
            config.SpecId = ClassSpecCatalog.NormalizeSpecForClass(config.BaseClassId, null);
            characterNameField = GetDefaultNameForSlot(0);
        }
        else
        {
            var idx = Math.Clamp(config.SelectedSlot, 0, filled.Count - 1);
            config.SelectedSlot = filled[idx];
            EnsureSelectedSlotLoaded();
        }

        config.Save();
        statusText = "Slot deleted.";
    }

    private void ConnectFromSelectedSlot()
    {
        SaveConnectionFields();
        if (!slotStore.TryLoadSlot(config.AccountSubject, config.SelectedSlot, out var slot))
        {
            statusText = "Cannot play: selected slot is empty.";
            return;
        }

        config.CharacterName = slot!.Name;
        config.BaseClassId = slot.BaseClassId;
        config.SpecId = slot.SpecId;

        if (!int.TryParse(portField, out var parsedPort))
        {
            statusText = "Invalid port.";
            return;
        }

        if (netClient.IsConnected)
        {
            netClient.Dispose();
        }

        ResetSessionState();
        netClient.Connect(hostField.Trim(), parsedPort);
        netClient.Send(new ClientHello { ClientNonce = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue) });
        statusText = "Connecting...";
        LogCast("Connecting...");
    }

    private void ReturnToCharacterSelect()
    {
        DisconnectFromServer();
        screen = UiScreen.CharacterSelect;
        pauseMenuOpen = false;
        statusText = "Returned to character select.";
    }

    private void LogoutToLogin()
    {
        DisconnectFromServer();
        screen = UiScreen.Login;
        pauseMenuOpen = false;
        statusText = "Logged out.";
    }

    private void DisconnectFromServer()
    {
        if (netClient.IsConnected)
        {
            netClient.Send(new DisconnectRequest());
            netClient.Dispose();
        }

        ResetSessionState();
    }

    private void SaveConnectionFields()
    {
        config.Host = string.IsNullOrWhiteSpace(hostField) ? "127.0.0.1" : hostField.Trim();
        if (int.TryParse(portField, out var p))
        {
            config.Port = Math.Clamp(p, 1, 65535);
        }
        portField = config.Port.ToString();
        hostField = config.Host;
        config.Save();
    }

    private void EnsureSelectedSlotLoaded()
    {
        hostField = config.Host;
        portField = config.Port.ToString();
        loginUsername = config.AccountDisplayName;

        if (slotStore.TryLoadSlot(config.AccountSubject, config.SelectedSlot, out var slot))
        {
            config.CharacterName = slot!.Name;
            config.BaseClassId = slot.BaseClassId;
            config.SpecId = slot.SpecId;
            characterNameField = slot.Name;
        }
        else
        {
            characterNameField = GetDefaultNameForSlot(config.SelectedSlot);
            config.BaseClassId = ClassSpecCatalog.NormalizeBaseClass(config.BaseClassId);
            config.SpecId = ClassSpecCatalog.NormalizeSpecForClass(config.BaseClassId, config.SpecId);
        }

        config.Save();
    }

    private string GetSlotSummary(int slot)
    {
        if (!slotStore.TryLoadSlot(config.AccountSubject, slot, out var record))
        {
            return $"Slot {slot}: Empty";
        }

        return $"Slot {slot}: {record!.Name} ({record.BaseClassId} / {record.SpecId.Replace("spec.", string.Empty)})";
    }

    private static string GetDefaultNameForSlot(int slot)
    {
        return slot switch
        {
            0 => "Warrior",
            1 => "Mage",
            2 => "Ranger",
            _ => $"Character {slot + 1}"
        };
    }

    private void AddButton(int x, int y, int w, int h, string label, Action onClick, bool enabled = true)
    {
        uiButtons.Add(new UiButton(new Rectangle(x, y, w, h), label, onClick, enabled));
    }

    private readonly List<UiTextField> textFields = new();

    private void DrawTextField(SpriteBatch batch, SpriteFont font, string label, string id, string value, Rectangle rect, bool password)
    {
        textFields.RemoveAll(f => f.Id == id);
        textFields.Add(new UiTextField(id, rect));

        batch.DrawString(font, label, new Vector2(rect.X, rect.Y - 22), Color.White);
        batch.Draw(pixel!, rect, new Color(7, 19, 40, 240));
        DrawOutline(batch, rect, activeTextField == id ? Color.Yellow : Color.White);

        var display = password ? new string('*', value.Length) : value;
        batch.DrawString(font, display, new Vector2(rect.X + 6, rect.Y + 6), Color.White);
    }

    private void DrawSelectable(SpriteBatch batch, SpriteFont font, Rectangle rect, string label, bool selected, Action onClick)
    {
        AddButton(rect.X, rect.Y, rect.Width, rect.Height, label, onClick, true);
        batch.Draw(pixel!, rect, selected ? new Color(24, 68, 132, 255) : new Color(9, 26, 52, 240));
        DrawOutline(batch, rect, selected ? Color.Yellow : Color.White);
        batch.DrawString(font, label, new Vector2(rect.X + 8, rect.Y + 7), Color.White);
    }

    private void DrawButtons(SpriteBatch batch, SpriteFont font)
    {
        for (var i = 0; i < uiButtons.Count; i++)
        {
            var b = uiButtons[i];
            batch.Draw(pixel!, b.Bounds, b.Enabled ? new Color(16, 48, 92, 230) : new Color(28, 36, 52, 220));
            DrawOutline(batch, b.Bounds, b.Enabled ? Color.White : Color.Gray);
            batch.DrawString(font, b.Label, new Vector2(b.Bounds.X + 8, b.Bounds.Y + 7), b.Enabled ? Color.White : Color.LightGray);
        }
    }

    private static void DrawCentered(SpriteBatch batch, SpriteFont font, string text, int x, int y, int width, Color color)
    {
        var size = font.MeasureString(text);
        var tx = x + (width - size.X) * 0.5f;
        batch.DrawString(font, text, new Vector2(tx, y), color);
    }

    private void DrawOutline(SpriteBatch batch, Rectangle rect, Color color)
    {
        batch.Draw(pixel!, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        batch.Draw(pixel!, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        batch.Draw(pixel!, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        batch.Draw(pixel!, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }

    private void DrawLinkLine(SpriteBatch batch, Texture2D px, Vector2 cameraPos, uint ownerId, uint targetId)
    {
        if (!renderEntities.TryGetValue(ownerId, out var ownerPos) || !renderEntities.TryGetValue(targetId, out var targetPos))
        {
            return;
        }

        var a = WorldToScreen(ownerPos, cameraPos);
        var b = WorldToScreen(targetPos, cameraPos);
        var delta = b - a;
        var len = delta.Length();
        if (len < 1f)
        {
            return;
        }

        var angle = MathF.Atan2(delta.Y, delta.X);
        batch.Draw(px, a, null, Color.Magenta, angle, Vector2.Zero, new Vector2(len, 2f), SpriteEffects.None, 0f);
    }

    private static float ResolveSize(EntityKind kind)
    {
        return kind switch
        {
            EntityKind.Player => 30f,
            EntityKind.Enemy => 34f,
            EntityKind.Loot => 24f,
            EntityKind.Zone => 54f,
            EntityKind.Link => 16f,
            _ => 22f
        };
    }

    private Color ResolveColor(uint entityId, EntityKind kind)
    {
        return kind switch
        {
            EntityKind.Player when entityId == localEntityId => Color.Lime,
            EntityKind.Player => Color.Cyan,
            EntityKind.Enemy => Color.Red,
            EntityKind.Loot => Color.Gold,
            EntityKind.Zone => new Color(90, 210, 255, 180),
            EntityKind.Link => Color.Magenta,
            _ => Color.White
        };
    }

    private static bool WasPressed(KeyboardState current, KeyboardState previous, Keys key)
    {
        return current.IsKeyDown(key) && !previous.IsKeyDown(key);
    }

    private static bool WasPressed(MouseState current, MouseState previous, bool left)
    {
        return left
            ? current.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released
            : current.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released;
    }

    private InputActionFlags ReadActionFlags(KeyboardState keyboard, MouseState mouse)
    {
        var flags = InputActionFlags.None;
        if (mouse.LeftButton == ButtonState.Pressed)
        {
            flags |= InputActionFlags.FastAttackHold;
        }

        if (mouse.RightButton == ButtonState.Pressed)
        {
            flags |= InputActionFlags.HeavyAttackHold;
        }

        if (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift))
        {
            flags |= InputActionFlags.BlockHold;
        }

        if (WasPressed(keyboard, previousKeyboard, Keys.E)) flags |= InputActionFlags.Skill1;
        if (WasPressed(keyboard, previousKeyboard, Keys.R)) flags |= InputActionFlags.Skill2;
        if (WasPressed(keyboard, previousKeyboard, Keys.Q)) flags |= InputActionFlags.Skill3;
        if (WasPressed(keyboard, previousKeyboard, Keys.T)) flags |= InputActionFlags.Skill4;
        if (WasPressed(keyboard, previousKeyboard, Keys.D1)) flags |= InputActionFlags.Skill5;
        if (WasPressed(keyboard, previousKeyboard, Keys.D2)) flags |= InputActionFlags.Skill6;
        if (WasPressed(keyboard, previousKeyboard, Keys.D3)) flags |= InputActionFlags.Skill7;
        if (WasPressed(keyboard, previousKeyboard, Keys.D4)) flags |= InputActionFlags.Skill8;

        if (interactPressedBufferedForSim)
        {
            flags |= InputActionFlags.InteractPortal;
            interactPressedBufferedForSim = false;
        }

        return flags;
    }

    private static Vector2 ReadMovementInput(KeyboardState keyboard)
    {
        var x = 0f;
        var y = 0f;
        if (keyboard.IsKeyDown(Keys.A)) x -= 1f;
        if (keyboard.IsKeyDown(Keys.D)) x += 1f;
        if (keyboard.IsKeyDown(Keys.S)) y -= 1f;
        if (keyboard.IsKeyDown(Keys.W)) y += 1f;

        var v = new Vector2(x, y);
        if (v.LengthSquared() > 1f)
        {
            v.Normalize();
        }

        return v;
    }

    private void IntegrateLocal(short inputX, short inputY)
    {
        var deltaX = inputX * moveSpeedMilliPerSecond / (InputScale * SimulationHz);
        var deltaY = inputY * moveSpeedMilliPerSecond / (InputScale * SimulationHz);
        predictedLocalXMilli = Math.Clamp(predictedLocalXMilli + deltaX, -WorldBoundaryMilli, WorldBoundaryMilli);
        predictedLocalYMilli = Math.Clamp(predictedLocalYMilli + deltaY, -WorldBoundaryMilli, WorldBoundaryMilli);
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

    private static Vector2 MilliToVector(int xMilli, int yMilli)
    {
        return new Vector2(xMilli / 1000f, yMilli / 1000f);
    }

    private Vector2 GetCameraPosition()
    {
        if (localEntityId != 0 && renderEntities.TryGetValue(localEntityId, out var pos))
        {
            return pos;
        }

        if (localEntityId != 0 && latestEntities.TryGetValue(localEntityId, out var latest))
        {
            return latest;
        }

        return Vector2.Zero;
    }

    private Vector2 WorldToScreen(Vector2 world, Vector2 camera)
    {
        var center = new Vector2(GraphicsDevice.Viewport.Width * 0.5f, GraphicsDevice.Viewport.Height * 0.62f);
        return new Vector2(
            center.X + (world.X - camera.X) * worldZoom,
            center.Y - (world.Y - camera.Y) * worldZoom);
    }

    private Vector2 ScreenToWorld(Vector2 screen, Vector2 camera)
    {
        var center = new Vector2(GraphicsDevice.Viewport.Width * 0.5f, GraphicsDevice.Viewport.Height * 0.62f);
        return new Vector2(
            camera.X + (screen.X - center.X) / worldZoom,
            camera.Y - (screen.Y - center.Y) / worldZoom);
    }

    private static string ResolveSlotLabel(byte slotCode)
    {
        return slotCode switch
        {
            0 => "LMB",
            1 => "RMB",
            2 => "Shift",
            3 => "E",
            4 => "R",
            5 => "Q",
            6 => "T",
            7 => "1",
            8 => "2",
            9 => "3",
            10 => "4",
            _ => "Cast"
        };
    }

    private static string FormatCastFeedback(byte slotCode, byte resultCode)
    {
        var slot = ResolveSlotLabel(slotCode);
        var outcome = resultCode switch
        {
            1 => "success",
            2 => "failed: no target",
            3 => "failed: cooldown",
            4 => "failed: insufficient resource",
            _ => "failed"
        };

        return $"{slot} {outcome}";
    }

    private void RecordActionStarts(InputActionFlags actionFlags)
    {
        var started = actionFlags & ~previousActionFlags;
        if (started == InputActionFlags.None)
        {
            return;
        }

        if ((started & InputActionFlags.HeavyAttackHold) != 0) LogCombat("RMB Heavy Attack");
        if ((started & InputActionFlags.BlockHold) != 0) LogCombat("Shift Block");
        if ((started & InputActionFlags.Skill1) != 0) LogCombat("Cast E");
        if ((started & InputActionFlags.Skill2) != 0) LogCombat("Cast R");
        if ((started & InputActionFlags.Skill3) != 0) LogCombat("Cast Q");
        if ((started & InputActionFlags.Skill4) != 0) LogCombat("Cast T");
        if ((started & InputActionFlags.Skill5) != 0) LogCombat("Cast 1");
        if ((started & InputActionFlags.Skill6) != 0) LogCombat("Cast 2");
        if ((started & InputActionFlags.Skill7) != 0) LogCombat("Cast 3");
        if ((started & InputActionFlags.Skill8) != 0) LogCombat("Cast 4");
    }

    private void LogCombat(string text)
    {
        combatFeed.Add(text);
        if (combatFeed.Count > MaxFeedEntries)
        {
            combatFeed.RemoveAt(0);
        }
    }

    private void LogCast(string text)
    {
        castFeed.Add(text);
        if (castFeed.Count > MaxFeedEntries)
        {
            castFeed.RemoveAt(0);
        }
    }

    private string FmtCd(int idx)
    {
        return localCooldownTicks[idx] == 0 ? "-" : localCooldownTicks[idx].ToString();
    }

    private JoinOverworldRequest BuildJoinRequest(ClientConfig cfg)
    {
        return new JoinOverworldRequest
        {
            AccountSubject = string.IsNullOrWhiteSpace(cfg.AccountSubject) ? "local:guest" : cfg.AccountSubject,
            AccountDisplayName = string.IsNullOrWhiteSpace(cfg.AccountDisplayName) ? "Guest" : cfg.AccountDisplayName,
            CharacterSlot = Math.Clamp(cfg.SelectedSlot, 0, 5),
            CharacterName = string.IsNullOrWhiteSpace(cfg.CharacterName) ? $"Character {cfg.SelectedSlot + 1}" : cfg.CharacterName,
            BaseClassId = ClassSpecCatalog.NormalizeBaseClass(cfg.BaseClassId),
            SpecId = ClassSpecCatalog.NormalizeSpecForClass(cfg.BaseClassId, cfg.SpecId)
        };
    }

    private void ResetSessionState()
    {
        latestEntities.Clear();
        renderEntities.Clear();
        latestEntityKinds.Clear();
        latestEntityHealth.Clear();
        latestEntityBuilderResource.Clear();
        latestEntitySpenderResource.Clear();
        latestLinkOwnerByEntity.Clear();
        latestLinkTargetByEntity.Clear();
        latestLinkRemainingTicks.Clear();
        latestEnemyAggroTarget.Clear();
        latestEnemyAggroThreat.Clear();
        latestEnemyForcedTicks.Clear();
        latestEnemyPrimaryStatusStacks.Clear();
        lootCurrencyById.Clear();
        remoteEntitySamples.Clear();
        pendingInputs.Clear();

        localEntityId = 0;
        currentInstanceId = 0;
        inputSequence = 0;
        localClientTick = 0;
        localTickAccumulator = 0f;
        joined = false;
        hasSnapshot = false;
        hasLocalPredictionState = false;
        predictedLocalXMilli = 0;
        predictedLocalYMilli = 0;
        localActionFlags = InputActionFlags.None;
        previousActionFlags = InputActionFlags.None;
        localMoveInputVector = Vector2.Zero;
        localHealth = 0;
        localBuilder = 0;
        localSpender = 0;
        localCurrency = 0;
        currentZone = ZoneKind.Overworld;

        lastSeenCastSlot = 0;
        lastSeenCastResult = 0;
        lastSeenCastTargetTeam = 0;
        lastSeenCastAffected = 0;
        lastSeenCastVfx = 0;
        lastSeenCastFeedbackTicks = 0;

        pickupPressedBuffered = false;
        fastAttackTapTicksRemaining = 0;
        pickupIntentTicksRemaining = 0;
        suppressFastAttackTicks = 0;
        interactPressedBufferedForSim = false;
        interactPressedBufferedForZone = false;
        returnHomePressedBuffered = false;
        settingsReturnToInGame = false;

        Array.Clear(localCooldownTicks, 0, localCooldownTicks.Length);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private readonly record struct PendingInput(uint Sequence, short MoveX, short MoveY)
    {
        public uint Sequence { get; init; } = Sequence;
        public short MoveX { get; init; } = MoveX;
        public short MoveY { get; init; } = MoveY;
    }

    private struct SnapshotSample
    {
        public float ReceivedAtSeconds;
        public Vector2 Position;
    }

    private readonly record struct UiButton(Rectangle Bounds, string Label, Action OnClick, bool Enabled);
    private readonly record struct UiTextField(string Id, Rectangle Bounds);
}

internal sealed class UdpProtocolClient : IDisposable
{
    private readonly ConcurrentQueue<IProtocolMessage> inbox = new();
    private UdpClient? udp;
    private IPEndPoint? endpoint;
    private CancellationTokenSource? cts;

    public bool IsConnected => udp is not null;

    public void Connect(string host, int port)
    {
        if (udp is not null)
        {
            return;
        }

        cts = new CancellationTokenSource();
        udp = new UdpClient(0);
        endpoint = new IPEndPoint(IPAddress.Parse(host), port);
        _ = ReceiveLoopAsync(cts.Token);
    }

    public void Send(IProtocolMessage message)
    {
        if (udp is null || endpoint is null)
        {
            return;
        }

        try
        {
            var payload = ProtocolCodec.Encode(message);
            udp.Send(payload, payload.Length, endpoint);
        }
        catch
        {
        }
    }

    public bool TryDequeue(out IProtocolMessage message)
    {
        return inbox.TryDequeue(out message!);
    }

    public void Dispose()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
        udp?.Dispose();
        udp = null;
        endpoint = null;
        while (inbox.TryDequeue(out _))
        {
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (udp is null)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct);
                if (ProtocolCodec.TryDecode(result.Buffer, out var msg) && msg is not null)
                {
                    inbox.Enqueue(msg);
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
}

internal sealed class ClientConfig
{
    private const string DefaultPathName = "config.json";

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 9000;
    public string AccountSubject { get; set; } = "local:dev-account";
    public string AccountDisplayName { get; set; } = "DevAccount";
    public int SelectedSlot { get; set; }
    public string CharacterName { get; set; } = "Character 1";
    public string BaseClassId { get; set; } = ClassSpecCatalog.NormalizeBaseClass(null);
    public string SpecId { get; set; } = ClassSpecCatalog.NormalizeSpecForClass(ClassSpecCatalog.NormalizeBaseClass(null), null);
    public float WorldZoom { get; set; } = 108f;
    public bool LinearWorldFiltering { get; set; } = false;

    public static ClientConfig Load()
    {
        var path = ConfigPath();
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<ClientConfig>(json);
                if (cfg is not null)
                {
                    cfg.BaseClassId = ClassSpecCatalog.NormalizeBaseClass(cfg.BaseClassId);
                    cfg.SpecId = ClassSpecCatalog.NormalizeSpecForClass(cfg.BaseClassId, cfg.SpecId);
                    cfg.SelectedSlot = Math.Clamp(cfg.SelectedSlot, 0, 5);
                    if (string.IsNullOrWhiteSpace(cfg.CharacterName))
                    {
                        cfg.CharacterName = $"Character {cfg.SelectedSlot + 1}";
                    }
                    cfg.WorldZoom = Math.Clamp(cfg.WorldZoom, 70f, 180f);

                    return cfg;
                }
            }
        }
        catch
        {
        }

        return new ClientConfig();
    }

    public void Save()
    {
        try
        {
            BaseClassId = ClassSpecCatalog.NormalizeBaseClass(BaseClassId);
            SpecId = ClassSpecCatalog.NormalizeSpecForClass(BaseClassId, SpecId);
            SelectedSlot = Math.Clamp(SelectedSlot, 0, 5);
            if (string.IsNullOrWhiteSpace(CharacterName))
            {
                CharacterName = $"Character {SelectedSlot + 1}";
            }
            WorldZoom = Math.Clamp(WorldZoom, 70f, 180f);

            var path = ConfigPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    public static string ConfigPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "Armament", "client-mg", DefaultPathName);
    }
}

internal sealed class CharacterSlotStore
{
    private const int MaxSlots = 6;
    private readonly Dictionary<string, CharacterSlotRecord?[]> slotsByAccount = new(StringComparer.OrdinalIgnoreCase);

    public static CharacterSlotStore Load()
    {
        var store = new CharacterSlotStore();
        var path = PathOnDisk();
        try
        {
            if (!File.Exists(path))
            {
                return store;
            }

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<CharacterSlotStoreDto>(json);
            if (dto?.Accounts is null)
            {
                return store;
            }

            foreach (var acc in dto.Accounts)
            {
                var arr = new CharacterSlotRecord?[MaxSlots];
                if (acc.Slots is not null)
                {
                    for (var i = 0; i < MaxSlots && i < acc.Slots.Count; i++)
                    {
                        var slot = acc.Slots[i];
                        if (slot is null || string.IsNullOrWhiteSpace(slot.Name))
                        {
                            continue;
                        }

                        slot.BaseClassId = ClassSpecCatalog.NormalizeBaseClass(slot.BaseClassId);
                        slot.SpecId = ClassSpecCatalog.NormalizeSpecForClass(slot.BaseClassId, slot.SpecId);
                        arr[i] = slot;
                    }
                }

                store.slotsByAccount[NormalizeSubject(acc.AccountSubject)] = arr;
            }
        }
        catch
        {
        }

        return store;
    }

    public void Save()
    {
        try
        {
            var dto = new CharacterSlotStoreDto
            {
                Accounts = slotsByAccount.Select(pair => new AccountSlotsDto
                {
                    AccountSubject = pair.Key,
                    Slots = pair.Value.ToList()
                }).ToList()
            };

            var path = PathOnDisk();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    public bool TryLoadSlot(string accountSubject, int slot, out CharacterSlotRecord? record)
    {
        var arr = EnsureAccountArray(accountSubject);
        slot = Math.Clamp(slot, 0, MaxSlots - 1);
        record = arr[slot];
        return record is not null && !string.IsNullOrWhiteSpace(record.Name);
    }

    public void SaveSlot(string accountSubject, int slot, CharacterSlotRecord record)
    {
        var arr = EnsureAccountArray(accountSubject);
        slot = Math.Clamp(slot, 0, MaxSlots - 1);
        arr[slot] = new CharacterSlotRecord
        {
            Name = record.Name.Trim(),
            BaseClassId = ClassSpecCatalog.NormalizeBaseClass(record.BaseClassId),
            SpecId = ClassSpecCatalog.NormalizeSpecForClass(record.BaseClassId, record.SpecId)
        };
    }

    public void DeleteSlotAndCompact(string accountSubject, int deletedSlot, int maxSlots)
    {
        var arr = EnsureAccountArray(accountSubject);
        var cap = Math.Clamp(maxSlots, 1, MaxSlots);
        deletedSlot = Math.Clamp(deletedSlot, 0, cap - 1);
        for (var i = deletedSlot; i < cap - 1; i++)
        {
            arr[i] = arr[i + 1];
        }

        arr[cap - 1] = null;
    }

    public List<int> GetFilledSlots(string accountSubject, int maxSlots)
    {
        var arr = EnsureAccountArray(accountSubject);
        var cap = Math.Clamp(maxSlots, 1, MaxSlots);
        var list = new List<int>(cap);
        for (var i = 0; i < cap; i++)
        {
            if (arr[i] is not null && !string.IsNullOrWhiteSpace(arr[i]!.Name))
            {
                list.Add(i);
            }
        }

        return list;
    }

    public int GetNextEmptySlot(string accountSubject, int maxSlots)
    {
        var arr = EnsureAccountArray(accountSubject);
        var cap = Math.Clamp(maxSlots, 1, MaxSlots);
        for (var i = 0; i < cap; i++)
        {
            if (arr[i] is null || string.IsNullOrWhiteSpace(arr[i]!.Name))
            {
                return i;
            }
        }

        return -1;
    }

    private CharacterSlotRecord?[] EnsureAccountArray(string subject)
    {
        var key = NormalizeSubject(subject);
        if (!slotsByAccount.TryGetValue(key, out var arr))
        {
            arr = new CharacterSlotRecord?[MaxSlots];
            slotsByAccount[key] = arr;
        }

        return arr;
    }

    public static string NormalizeSubject(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "local_guest";
        }

        return input.Trim().ToLowerInvariant()
            .Replace(":", "_")
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace(" ", "_");
    }

    private static string PathOnDisk()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "Armament", "client-mg", "character-slots.json");
    }
}

internal sealed class CharacterSlotRecord
{
    public string Name { get; set; } = string.Empty;
    public string BaseClassId { get; set; } = ClassSpecCatalog.NormalizeBaseClass(null);
    public string SpecId { get; set; } = ClassSpecCatalog.NormalizeSpecForClass(ClassSpecCatalog.NormalizeBaseClass(null), null);
}

internal sealed class CharacterSlotStoreDto
{
    public List<AccountSlotsDto> Accounts { get; set; } = new();
}

internal sealed class AccountSlotsDto
{
    public string AccountSubject { get; set; } = string.Empty;
    public List<CharacterSlotRecord?> Slots { get; set; } = new();
}
