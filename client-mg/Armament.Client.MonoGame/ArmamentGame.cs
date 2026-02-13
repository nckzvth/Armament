using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Armament.Client.MonoGame.Animation;
using Armament.SharedSim.Inventory;
using Armament.SharedSim.Protocol;
using Armament.SharedSim.Sim;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Armament.Client.MonoGame;

public sealed partial class ArmamentGame : Game
{
    private const int SimulationHz = 60;
    private const int InputScale = 1000;
    private const int WorldBoundaryMilli = 500_000;
    private const float InterpolationBackTimeSeconds = 0.10f;
    private const int MaxFeedEntries = 10;
    private const int MaxSlots = 6;
    private const uint PortalEntityId = 900_001;
    private const float DefaultWorldZoom = 108f;
    private const int InventoryCellSize = 34;
    private const int InventoryCellSpacing = 4;

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
    private readonly Dictionary<uint, string> latestEnemyArchetypeByEntity = new();
    private readonly Dictionary<uint, uint> latestEnemyAggroTarget = new();
    private readonly Dictionary<uint, ushort> latestEnemyAggroThreat = new();
    private readonly Dictionary<uint, byte> latestEnemyForcedTicks = new();
    private readonly Dictionary<uint, byte> latestEnemyPrimaryStatusStacks = new();
    private readonly Dictionary<uint, ushort> lootCurrencyById = new();
    private readonly Dictionary<uint, WorldZoneSnapshot> latestZones = new();
    private readonly Dictionary<uint, WorldLinkSnapshot> latestLinks = new();
    private readonly Dictionary<uint, WorldObjectSnapshot> latestWorldObjects = new();
    private readonly Dictionary<uint, WorldHazardSnapshot> latestHazards = new();
    private readonly Dictionary<uint, WorldNpcSnapshot> latestNpcs = new();
    private readonly HashSet<string> latestActiveObjectiveTargets = new(StringComparer.Ordinal);

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
    private bool inventoryPanelOpen;
    private bool questLogOpen;
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
    private readonly InventorySnapshot uiInventory;
    private readonly DictionaryInventoryItemCatalog uiInventoryCatalog;
    private InventoryItemInstance? heldInventoryItem;
    private EquipSlot? heldFromEquipSlot;
    private GridCoord? heldFromBackpackCell;
    private readonly HashSet<string> inventoryLayoutIssuesSeen = new(StringComparer.Ordinal);
    private readonly List<QuestLogActState> questLogActs = new();
    private int selectedQuestActIndex;
    private int selectedQuestZoneIndex;
    private int selectedQuestIndex;
    private static readonly EquipSlot[] RequiredInventorySlotOrder =
    {
        EquipSlot.MainHand, EquipSlot.OffHand, EquipSlot.Chest, EquipSlot.Legs,
        EquipSlot.Head, EquipSlot.Hands, EquipSlot.Belt, EquipSlot.Feet,
        EquipSlot.Ring1, EquipSlot.Ring2, EquipSlot.Amulet, EquipSlot.Relic
    };

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

        uiInventoryCatalog = BuildUiInventoryCatalog();
        uiInventory = new InventorySnapshot
        {
            BackpackWidth = 12,
            BackpackHeight = 8
        };
        InventoryRules.EnsureBackpackSize(uiInventory);
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

        if (screen == UiScreen.InGame && WasPressed(keyboard, previousKeyboard, Keys.I))
        {
            inventoryPanelOpen = !inventoryPanelOpen;
        }

        if (screen == UiScreen.InGame && WasPressed(keyboard, previousKeyboard, Keys.J))
        {
            questLogOpen = !questLogOpen;
        }

        if (screen == UiScreen.InGame && questLogOpen)
        {
            HandleQuestLogInput(keyboard, mouse);
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
        var inventoryIntercept = inventoryPanelOpen;
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

        var lmbPressed = !inventoryIntercept && WasPressed(mouse, previousMouse, true);
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

        var actionFlags = ReadActionFlags(keyboard, mouse, inventoryPanelOpen || IsPointerOverInventoryModal(mouse));
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
        latestEnemyArchetypeByEntity.Clear();
        latestEnemyAggroTarget.Clear();
        latestEnemyAggroThreat.Clear();
        latestEnemyForcedTicks.Clear();
        latestEnemyPrimaryStatusStacks.Clear();
        lootCurrencyById.Clear();
        latestZones.Clear();
        latestLinks.Clear();
        latestWorldObjects.Clear();
        latestHazards.Clear();
        latestNpcs.Clear();
        latestNpcs.Clear();
        latestActiveObjectiveTargets.Clear();

        currentZone = snapshot.ZoneKind;
        currentInstanceId = snapshot.InstanceId;
        RebuildQuestLog(snapshot.Objectives);
        latestActiveObjectiveTargets.Clear();
        for (var i = 0; i < snapshot.Objectives.Count; i++)
        {
            var objective = snapshot.Objectives[i];
            if (objective.State == 1 && !string.IsNullOrWhiteSpace(objective.TargetId))
            {
                latestActiveObjectiveTargets.Add(objective.TargetId);
            }
        }

        latestZones.Clear();
        for (var i = 0; i < snapshot.Zones.Count; i++)
        {
            var zone = snapshot.Zones[i];
            latestZones[zone.ZoneRuntimeId] = zone;
        }

        latestLinks.Clear();
        for (var i = 0; i < snapshot.Links.Count; i++)
        {
            var link = snapshot.Links[i];
            latestLinks[link.LinkRuntimeId] = link;
        }

        latestWorldObjects.Clear();
        for (var i = 0; i < snapshot.WorldObjects.Count; i++)
        {
            var obj = snapshot.WorldObjects[i];
            latestWorldObjects[obj.ObjectId] = obj;
        }

        latestHazards.Clear();
        for (var i = 0; i < snapshot.Hazards.Count; i++)
        {
            var hazard = snapshot.Hazards[i];
            latestHazards[hazard.HazardRuntimeId] = hazard;
        }

        latestNpcs.Clear();
        for (var i = 0; i < snapshot.Npcs.Count; i++)
        {
            var npc = snapshot.Npcs[i];
            latestNpcs[npc.NpcRuntimeId] = npc;
        }

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

            if (entity.Kind == EntityKind.Enemy)
            {
                latestEnemyArchetypeByEntity[entity.EntityId] = entity.ArchetypeId;
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
            if (pair.Value == EntityKind.Loot &&
                latestEntities.TryGetValue(pair.Key, out var pos))
            {
                renderEntities[pair.Key] = pos;
            }
        }
    }

    private void DrawWorld(SpriteBatch batch, Texture2D px, Vector2 cameraPos)
    {
        DrawCampaignHazards(batch, px, cameraPos);
        DrawCampaignWorldObjects(batch, px, cameraPos);
        DrawCampaignNpcs(batch, px, cameraPos);
        DrawZones(batch, px, cameraPos);

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

            if (kind == EntityKind.Loot && showLootNames && pair.Key != PortalEntityId && debugFont is not null)
            {
                var amount = lootCurrencyById.TryGetValue(pair.Key, out var currency) ? currency : (ushort)0;
                batch.DrawString(debugFont, amount > 0 ? $"Gold {amount}" : "Loot", screenPos + new Vector2(-22f, -30f), Color.White);
            }
        }

        DrawLinks(batch, px, cameraPos);
        DrawPortalPrompt(batch, cameraPos);
    }

    private void DrawZones(SpriteBatch batch, Texture2D px, Vector2 cameraPos)
    {
        foreach (var zone in latestZones.Values)
        {
            var worldPos = new Vector2(
                Quantization.DequantizePosition(zone.QuantizedX),
                Quantization.DequantizePosition(zone.QuantizedY));
            var screenPos = WorldToScreen(worldPos, cameraPos);
            var size = Math.Clamp(zone.RadiusDeciUnits / 2f, 26f, 120f);
            var rect = new Rectangle((int)(screenPos.X - size * 0.5f), (int)(screenPos.Y - size * 0.5f), (int)size, (int)size);
            batch.Draw(px, rect, new Color(90, 210, 255, 96));
            DrawOutline(batch, rect, Color.LightBlue);
        }
    }

    private void DrawLinks(SpriteBatch batch, Texture2D px, Vector2 cameraPos)
    {
        foreach (var link in latestLinks.Values)
        {
            DrawLinkLine(batch, px, cameraPos, link.OwnerEntityId, link.TargetEntityId);
        }
    }

    private void DrawCampaignHazards(SpriteBatch batch, Texture2D px, Vector2 cameraPos)
    {
        foreach (var hazard in latestHazards.Values)
        {
            var worldPos = new Vector2(
                Quantization.DequantizePosition(hazard.QuantizedX),
                Quantization.DequantizePosition(hazard.QuantizedY));
            var screenPos = WorldToScreen(worldPos, cameraPos);
            var radius = 48;
            var rect = new Rectangle((int)(screenPos.X - radius), (int)(screenPos.Y - radius), radius * 2, radius * 2);
            var tint = ResolveHazardColor(hazard.HazardId);
            batch.Draw(px, rect, tint * 0.35f);
            DrawOutline(batch, rect, tint);

            if (debugFont is not null && showVerboseHud)
            {
                batch.DrawString(debugFont, $"{hazard.HazardId} ({hazard.RemainingTicks})", screenPos + new Vector2(-52f, -64f), new Color(245, 235, 190));
            }
        }
    }

    private void DrawCampaignWorldObjects(SpriteBatch batch, Texture2D px, Vector2 cameraPos)
    {
        foreach (var obj in latestWorldObjects.Values)
        {
            var worldPos = new Vector2(
                Quantization.DequantizePosition(obj.QuantizedX),
                Quantization.DequantizePosition(obj.QuantizedY));
            var screenPos = WorldToScreen(worldPos, cameraPos);
            var size = 28f;
            var rect = new Rectangle((int)(screenPos.X - size * 0.5f), (int)(screenPos.Y - size * 0.5f), (int)size, (int)size);
            var color = ResolveObjectColor(obj.Archetype, obj.ObjectiveState);
            batch.Draw(px, rect, color);
            DrawOutline(batch, rect, new Color(220, 228, 238, 220));

            var isTrackedObjective = latestActiveObjectiveTargets.Contains(obj.ObjectDefId);
            if (isTrackedObjective)
            {
                var marker = new Rectangle(rect.X - 6, rect.Y - 12, rect.Width + 12, rect.Height + 12);
                DrawOutline(batch, marker, new Color(245, 210, 110, 240));
            }

            if (debugFont is not null)
            {
                var hp = $"{obj.Health}/{obj.MaxHealth}";
                batch.DrawString(debugFont, hp, screenPos + new Vector2(-18f, -26f), new Color(238, 244, 252));
                if (showVerboseHud)
                {
                    batch.DrawString(debugFont, obj.ObjectDefId, screenPos + new Vector2(-34f, 22f), new Color(200, 208, 225));
                }
            }
        }
    }

    private void DrawCampaignNpcs(SpriteBatch batch, Texture2D px, Vector2 cameraPos)
    {
        foreach (var npc in latestNpcs.Values)
        {
            var worldPos = new Vector2(
                Quantization.DequantizePosition(npc.QuantizedX),
                Quantization.DequantizePosition(npc.QuantizedY));
            var screenPos = WorldToScreen(worldPos, cameraPos);
            var size = 30f;
            var rect = new Rectangle((int)(screenPos.X - size * 0.5f), (int)(screenPos.Y - size * 0.5f), (int)size, (int)size);
            var color = new Color(148, 182, 236, 235);
            batch.Draw(px, rect, color);
            DrawOutline(batch, rect, new Color(235, 240, 252, 220));

            if (latestActiveObjectiveTargets.Contains(npc.NpcId))
            {
                var marker = new Rectangle(rect.X - 8, rect.Y - 14, rect.Width + 16, rect.Height + 16);
                DrawOutline(batch, marker, new Color(245, 210, 110, 240));
            }

            if (debugFont is not null)
            {
                batch.DrawString(debugFont, npc.Name, screenPos + new Vector2(-32f, -28f), new Color(236, 242, 252));
                if (showVerboseHud)
                {
                    batch.DrawString(debugFont, npc.NpcId, screenPos + new Vector2(-32f, 22f), new Color(200, 208, 225));
                }
            }
        }
    }

    private static Color ResolveHazardColor(string hazardId)
    {
        if (hazardId.Contains("bell", StringComparison.OrdinalIgnoreCase))
        {
            return new Color(255, 205, 120, 220);
        }

        if (hazardId.Contains("gas", StringComparison.OrdinalIgnoreCase))
        {
            return new Color(140, 220, 150, 220);
        }

        return new Color(190, 125, 235, 220);
    }

    private static Color ResolveObjectColor(string archetype, byte objectiveState)
    {
        if (objectiveState == 2)
        {
            return new Color(70, 92, 70, 180);
        }

        return archetype.ToLowerInvariant() switch
        {
            "spawner" => new Color(185, 78, 78, 235),
            "interactable" => new Color(88, 145, 206, 235),
            "objective" => new Color(218, 175, 92, 235),
            _ => new Color(132, 132, 132, 235)
        };
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
        batch.DrawString(font, "J quest log | F10 debug", new Vector2(dock.Right - 210, dock.Y - 20), new Color(210, 220, 240, 220));

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
                $"Local Entity: {localEntityId} | Snapshot: {(hasSnapshot ? "yes" : "no")} | Prediction: {(hasLocalPredictionState ? "yes" : "no")} | Links {latestLinks.Count}",
                $"Move: {localMoveInputVector.X:0.00},{localMoveInputVector.Y:0.00} | Aim: {aimWorldPosition.X:0.00},{aimWorldPosition.Y:0.00}",
                "Controls: WASD | LMB/RMB/Shift | E/R/Q/T + 1/2/3/4 | Z/F/H | I inventory | J quest log | Alt names | Esc menu"
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

        if (inventoryPanelOpen)
        {
            DrawInventoryPanel(batch, font);
        }

        if (questLogOpen && !inventoryPanelOpen)
        {
            DrawQuestLogPanel(batch, font);
        }
    }

    private void DrawInventoryPanel(SpriteBatch batch, SpriteFont font)
    {
        var layout = BuildInventoryLayout();
        batch.Draw(pixel!, layout.Panel, new Color(4, 12, 24, 238));
        DrawOutline(batch, layout.Panel, new Color(160, 180, 215, 240));
        var titleY = layout.Panel.Y + 8;
        batch.DrawString(font, "Inventory [I]", new Vector2(layout.Panel.X + 10, titleY), Color.White);
        var instructions = "LMB drag-drop | RMB quick-equip";
        var instructionWidth = (int)MathF.Ceiling(font.MeasureString(instructions).X);
        var instructionX = Math.Max(layout.Panel.X + 150, layout.Panel.Right - 12 - instructionWidth);
        batch.DrawString(font, instructions, new Vector2(instructionX, titleY), new Color(196, 210, 236));

        ValidateAndReportInventoryLayout(layout, font, instructions, instructionX);

        foreach (var kv in layout.EquipRects)
        {
            var slotRect = kv.Value;
            var slot = kv.Key;
            batch.Draw(pixel!, slotRect, new Color(14, 28, 50, 235));
            DrawOutline(batch, slotRect, new Color(92, 116, 150, 220));
            var slotLabel = SlotIndexLabel(slot);
            batch.DrawString(font, slotLabel, new Vector2(slotRect.X + 5, slotRect.Y + 4), new Color(204, 220, 245));
        }

        // Backpack grid.
        for (var y = 0; y < uiInventory.BackpackHeight; y++)
        {
            for (var x = 0; x < uiInventory.BackpackWidth; x++)
            {
                var c = new Rectangle(
                    layout.GridRect.X + x * (InventoryCellSize + InventoryCellSpacing),
                    layout.GridRect.Y + y * (InventoryCellSize + InventoryCellSpacing),
                    InventoryCellSize,
                    InventoryCellSize);
                batch.Draw(pixel!, c, new Color(12, 23, 42, 240));
                DrawOutline(batch, c, new Color(68, 90, 126, 210));
            }
        }

        var sectionHeaderY = titleY + 44;
        batch.DrawString(font, "Paper Doll", new Vector2(layout.EquipRegion.X + 2, sectionHeaderY), new Color(190, 206, 230));
        batch.DrawString(font, "Backpack", new Vector2(layout.GridRect.X + 2, layout.GridRect.Y - 40), new Color(190, 206, 230));
        DrawInventoryItems(batch, font, layout);
    }

    private void DrawInventoryItems(SpriteBatch batch, SpriteFont font, InventoryUiLayout layout)
    {
        var mouse = Mouse.GetState();
        DrawHeldDropPreview(batch, layout, mouse);

        for (var i = 0; i < uiInventory.BackpackItems.Count; i++)
        {
            var item = uiInventory.BackpackItems[i];
            if (!TryGetItemDrawRect(item, layout, out var rect))
            {
                continue;
            }

            DrawItemCard(batch, font, rect, item, heldInventoryItem is not null && string.Equals(heldInventoryItem.InstanceId, item.InstanceId, StringComparison.Ordinal), false, showName: false);
        }

        foreach (var kv in uiInventory.Equipment)
        {
            if (kv.Value is null || !layout.EquipRects.TryGetValue(kv.Key, out var slotRect))
            {
                continue;
            }

            var inset = new Rectangle(slotRect.X + 2, slotRect.Y + 2, Math.Max(1, slotRect.Width - 4), Math.Max(1, slotRect.Height - 4));
            DrawItemCard(batch, font, inset, kv.Value, heldInventoryItem is not null && string.Equals(heldInventoryItem.InstanceId, kv.Value.InstanceId, StringComparison.Ordinal), true, showName: false);
        }

        if (heldInventoryItem is not null)
        {
            if (uiInventoryCatalog.TryGet(heldInventoryItem.ItemCode, out var def))
            {
                var width = InventoryRules.ResolveItemWidth(def);
                var height = InventoryRules.ResolveItemHeight(def);
                var w = width * InventoryCellSize + (width - 1) * InventoryCellSpacing;
                var h = height * InventoryCellSize + (height - 1) * InventoryCellSpacing;
                var dragRect = new Rectangle(mouse.X - w / 2, mouse.Y - h / 2, w, h);
                DrawItemCard(batch, font, dragRect, heldInventoryItem, false, false, showName: false, alpha: 0.75f);
            }
        }

        DrawHoveredItemTooltip(batch, font, layout, mouse);
    }

    private void DrawItemCard(SpriteBatch batch, SpriteFont font, Rectangle rect, InventoryItemInstance item, bool selected, bool equipped, bool showName, float alpha = 1f)
    {
        var baseColor = equipped ? new Color(64, 68, 46) : new Color(46, 64, 88);
        var fill = Color.Lerp(baseColor, Color.Black, 0.25f);
        fill *= alpha;
        batch.Draw(pixel!, rect, fill);
        DrawOutline(batch, rect, selected ? new Color(245, 236, 148) : new Color(164, 182, 214, 228));

        if (showName)
        {
            var rawLabel = BuildItemLabel(item.ItemCode);
            var maxChars = Math.Max(4, (rect.Width - 8) / 10);
            var label = FitLabel(rawLabel, maxChars);
            batch.DrawString(font, label, new Vector2(rect.X + 4, rect.Y + 4), new Color(238, 242, 250) * alpha);
        }

        if (item.Quantity > 1)
        {
            batch.DrawString(font, item.Quantity.ToString(), new Vector2(rect.Right - 22, rect.Bottom - 22), new Color(255, 255, 255) * alpha);
        }
    }

    private void DrawHeldDropPreview(SpriteBatch batch, InventoryUiLayout layout, MouseState mouse)
    {
        if (heldInventoryItem is null || !uiInventoryCatalog.TryGet(heldInventoryItem.ItemCode, out var heldDef))
        {
            return;
        }

        var point = mouse.Position;
        if (TryResolveBackpackCell(layout, point, out var cell))
        {
            var width = InventoryRules.ResolveItemWidth(heldDef);
            var height = InventoryRules.ResolveItemHeight(heldDef);
            var rect = BuildCellFootprintRect(layout, cell, width, height);
            var canDrop = InventoryRules.CanPlaceAt(uiInventory, uiInventoryCatalog, heldInventoryItem.ItemCode, cell);
            var overlay = canDrop ? new Color(35, 180, 90, 70) : new Color(190, 30, 30, 85);
            var border = canDrop ? new Color(92, 230, 128, 220) : new Color(250, 82, 82, 230);
            batch.Draw(pixel!, rect, overlay);
            DrawOutline(batch, rect, border);
            return;
        }

        if (TryResolveEquipSlot(layout, point, out var slot) && layout.EquipRects.TryGetValue(slot, out var slotRect))
        {
            var canEquip = CanEquipHeldToSlot(slot);
            var overlay = canEquip ? new Color(35, 180, 90, 70) : new Color(190, 30, 30, 85);
            var border = canEquip ? new Color(92, 230, 128, 220) : new Color(250, 82, 82, 230);
            batch.Draw(pixel!, slotRect, overlay);
            DrawOutline(batch, slotRect, border);
        }
    }

    private void DrawHoveredItemTooltip(SpriteBatch batch, SpriteFont font, InventoryUiLayout layout, MouseState mouse)
    {
        if (!layout.Panel.Contains(mouse.Position))
        {
            return;
        }

        InventoryItemInstance? hovered = null;
        if (TryResolveBackpackCell(layout, mouse.Position, out var cell) &&
            InventoryRules.TryGetItemAtCell(uiInventory, uiInventoryCatalog, cell, out var bagItem, out _))
        {
            hovered = bagItem;
        }
        else if (TryResolveEquipSlot(layout, mouse.Position, out var slot) &&
                 uiInventory.Equipment.TryGetValue(slot, out var equipped) &&
                 equipped is not null)
        {
            hovered = equipped;
        }

        if (hovered is null || !uiInventoryCatalog.TryGet(hovered.ItemCode, out var def))
        {
            return;
        }

        var name = BuildItemLabel(hovered.ItemCode);
        var line2 = $"{InventoryRules.ResolveItemWidth(def)}x{InventoryRules.ResolveItemHeight(def)}";
        var line3 = hovered.Quantity > 1 ? $"Qty {hovered.Quantity}" : "Qty 1";
        var tooltipW = 170;
        var tooltipH = 66;
        var x = Math.Min(graphics.PreferredBackBufferWidth - tooltipW - 8, mouse.X + 18);
        var y = Math.Min(graphics.PreferredBackBufferHeight - tooltipH - 8, mouse.Y + 16);
        var rect = new Rectangle(x, y, tooltipW, tooltipH);
        batch.Draw(pixel!, rect, new Color(9, 18, 34, 240));
        DrawOutline(batch, rect, new Color(171, 188, 220, 240));
        batch.DrawString(font, name, new Vector2(rect.X + 6, rect.Y + 6), new Color(240, 244, 252));
        batch.DrawString(font, line2, new Vector2(rect.X + 6, rect.Y + 26), new Color(202, 216, 240));
        batch.DrawString(font, line3, new Vector2(rect.X + 6, rect.Y + 44), new Color(202, 216, 240));
    }

    private Rectangle BuildCellFootprintRect(InventoryUiLayout layout, GridCoord cell, int width, int height)
    {
        var x = layout.GridRect.X + cell.X * (InventoryCellSize + InventoryCellSpacing);
        var y = layout.GridRect.Y + cell.Y * (InventoryCellSize + InventoryCellSpacing);
        var w = width * InventoryCellSize + (width - 1) * InventoryCellSpacing;
        var h = height * InventoryCellSize + (height - 1) * InventoryCellSpacing;
        return new Rectangle(x, y, w, h);
    }

    private bool TryGetItemDrawRect(InventoryItemInstance item, InventoryUiLayout layout, out Rectangle rect)
    {
        rect = default;
        if (!uiInventoryCatalog.TryGet(item.ItemCode, out var def))
        {
            return false;
        }

        var width = InventoryRules.ResolveItemWidth(def);
        var height = InventoryRules.ResolveItemHeight(def);
        var x = layout.GridRect.X + item.Position.X * (InventoryCellSize + InventoryCellSpacing);
        var y = layout.GridRect.Y + item.Position.Y * (InventoryCellSize + InventoryCellSpacing);
        var w = width * InventoryCellSize + (width - 1) * InventoryCellSpacing;
        var h = height * InventoryCellSize + (height - 1) * InventoryCellSpacing;
        rect = new Rectangle(x, y, w, h);
        return true;
    }

    private InventoryUiLayout BuildInventoryLayout()
    {
        var gridW = uiInventory.BackpackWidth * InventoryCellSize + (uiInventory.BackpackWidth - 1) * InventoryCellSpacing;
        var gridH = uiInventory.BackpackHeight * InventoryCellSize + (uiInventory.BackpackHeight - 1) * InventoryCellSpacing;
        var panelW = Math.Max(560, gridW + 34);
        var panelH = 42 + 240 + 36 + gridH + 14;
        var panelX = Math.Max(16, graphics.PreferredBackBufferWidth - panelW - 12);
        var panelY = 40;
        var panel = new Rectangle(panelX, panelY, panelW, panelH);
        var equipRegion = new Rectangle(panel.X + 20, panel.Y + 62, panel.Width - 40, 220);

        var slotW = 72;
        var tallH = 112;
        var medH = 84;
        var smallH = 52;
        var accW = 62;
        var accH = 34;
        var topY = equipRegion.Y + 24;
        var left1X = equipRegion.X + 6;   // 1
        var left2X = left1X + slotW + 8;  // slots 5/6
        var centerLeftX = left2X + slotW + 26; // 3
        var centerRightX = centerLeftX + slotW + 20; // 4
        var right1X = centerRightX + slotW + 26; // 7/8
        var right2X = right1X + slotW + 8; // 2
        var row2Y = topY + tallH + 8;
        var accY = topY + tallH + 20;

        var equipRects = new Dictionary<EquipSlot, Rectangle>
        {
            [EquipSlot.MainHand] = new Rectangle(left1X, topY, slotW, tallH),      // 1
            [EquipSlot.OffHand] = new Rectangle(right2X, topY, slotW, tallH),      // 2
            [EquipSlot.Chest] = new Rectangle(centerLeftX, topY, slotW, tallH),    // 3
            [EquipSlot.Legs] = new Rectangle(centerRightX, topY, slotW, tallH),    // 4
            [EquipSlot.Head] = new Rectangle(left2X, topY, slotW, medH),           // 5
            [EquipSlot.Hands] = new Rectangle(left2X, topY + medH + 6, slotW, smallH), // 6
            [EquipSlot.Belt] = new Rectangle(right1X, topY, slotW, medH),          // 7
            [EquipSlot.Feet] = new Rectangle(right1X, topY + medH + 6, slotW, smallH), // 8
            [EquipSlot.Ring1] = new Rectangle(centerLeftX, accY, accW, accH),      // 9
            [EquipSlot.Ring2] = new Rectangle(centerLeftX + accW + 8, accY, accW, accH), // 10
            [EquipSlot.Amulet] = new Rectangle(centerLeftX + (accW + 8) * 2, accY, accW, accH), // 11
            [EquipSlot.Relic] = new Rectangle(centerLeftX + (accW + 8) * 3, accY, accW, accH)   // 12
        };

        var equipBottom = equipRects.Values.Max(r => r.Bottom);
        var gridTop = Math.Max(equipBottom + 34, equipRegion.Bottom + 12);
        var gridRect = new Rectangle(panel.X + (panel.Width - gridW) / 2, gridTop, gridW, gridH);

        return new InventoryUiLayout(panel, equipRegion, gridRect, equipRects);
    }

    private void ValidateAndReportInventoryLayout(InventoryUiLayout layout, SpriteFont font, string instructions, int instructionX)
    {
        var issues = new List<string>();
        if (layout.EquipRects.Count != 12)
        {
            issues.Add($"slot count {layout.EquipRects.Count} != 12");
        }

        foreach (var required in RequiredInventorySlotOrder)
        {
            if (!layout.EquipRects.ContainsKey(required))
            {
                issues.Add($"missing slot {required}");
            }
        }

        if (!ContainsRect(layout.Panel, layout.EquipRegion))
        {
            issues.Add("equip region outside panel");
        }

        if (!ContainsRect(layout.Panel, layout.GridRect))
        {
            issues.Add("grid outside panel");
        }

        if (layout.GridRect.Top <= layout.EquipRects.Values.Max(r => r.Bottom))
        {
            issues.Add("grid overlaps paper-doll slots");
        }

        var values = layout.EquipRects.Values.ToArray();
        for (var i = 0; i < values.Length; i++)
        {
            if (!ContainsRect(layout.Panel, values[i]))
            {
                issues.Add($"slot out of panel: {i + 1}");
            }

            if (values[i].Intersects(layout.GridRect))
            {
                issues.Add($"slot intersects grid: {i + 1}");
            }

            for (var j = i + 1; j < values.Length; j++)
            {
                if (values[i].Intersects(values[j]))
                {
                    issues.Add($"slot overlap pair: {i + 1}/{j + 1}");
                }
            }
        }

        if (instructionX < layout.Panel.X + 150)
        {
            issues.Add("header instructions clipped");
        }

        var instructionWidth = (int)MathF.Ceiling(font.MeasureString(instructions).X);
        if (instructionX + instructionWidth > layout.Panel.Right - 8)
        {
            issues.Add("header instructions overflow");
        }

        for (var i = 0; i < issues.Count; i++)
        {
            if (!inventoryLayoutIssuesSeen.Add(issues[i]))
            {
                continue;
            }

            LogCast($"[INV-LAYOUT] {issues[i]}");
        }
    }

    private static bool ContainsRect(Rectangle outer, Rectangle inner)
    {
        return inner.Left >= outer.Left &&
               inner.Top >= outer.Top &&
               inner.Right <= outer.Right &&
               inner.Bottom <= outer.Bottom;
    }

    private static string BuildItemLabel(string itemCode)
    {
        var label = itemCode.StartsWith("item.", StringComparison.Ordinal) ? itemCode["item.".Length..] : itemCode;
        var at = label.LastIndexOf('.');
        if (at >= 0 && at < label.Length - 1)
        {
            label = label[(at + 1)..];
        }

        return label;
    }

    private static string FitLabel(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        return maxChars <= 3 ? value[..Math.Max(1, maxChars)] : value[..(maxChars - 3)] + "...";
    }

    private static string SlotIndexLabel(EquipSlot slot)
    {
        return slot switch
        {
            EquipSlot.MainHand => "1",
            EquipSlot.OffHand => "2",
            EquipSlot.Chest => "3",
            EquipSlot.Legs => "4",
            EquipSlot.Head => "5",
            EquipSlot.Hands => "6",
            EquipSlot.Belt => "7",
            EquipSlot.Feet => "8",
            EquipSlot.Ring1 => "9",
            EquipSlot.Ring2 => "10",
            EquipSlot.Amulet => "11",
            EquipSlot.Relic => "12",
            _ => "?"
        };
    }

    private void UpdateInventoryUi(MouseState mouse)
    {
        if (!inventoryPanelOpen)
        {
            return;
        }

        var leftPressed = WasPressed(mouse, previousMouse, true);
        var leftReleased = WasReleased(mouse, previousMouse, true);
        var rightPressed = WasPressed(mouse, previousMouse, false);
        if (!leftPressed && !leftReleased && !rightPressed)
        {
            return;
        }

        var layout = BuildInventoryLayout();
        var point = new Point(mouse.X, mouse.Y);
        var inPanel = layout.Panel.Contains(point);

        if (rightPressed && inPanel && heldInventoryItem is null)
        {
            HandleInventoryRightClick(layout, point);
            return;
        }

        if (leftPressed && inPanel && heldInventoryItem is null)
        {
            BeginInventoryDrag(layout, point);
            return;
        }

        if (leftReleased && heldInventoryItem is not null)
        {
            TryDropHeldItem(layout, point, inPanel);
        }
    }

    private void BeginInventoryDrag(InventoryUiLayout layout, Point point)
    {
        if (TryResolveBackpackCell(layout, point, out var sourceCell) &&
            InventoryRules.TryGetItemAtCell(uiInventory, uiInventoryCatalog, sourceCell, out var sourceItem, out var sourceIndex))
        {
            heldInventoryItem = sourceItem;
            heldFromBackpackCell = sourceItem.Position;
            heldFromEquipSlot = null;
            uiInventory.BackpackItems.RemoveAt(sourceIndex);
            return;
        }

        if (TryResolveEquipSlot(layout, point, out var sourceSlot) &&
            uiInventory.Equipment.TryGetValue(sourceSlot, out var equipped) &&
            equipped is not null)
        {
            heldInventoryItem = equipped;
            heldFromEquipSlot = sourceSlot;
            heldFromBackpackCell = null;
            uiInventory.Equipment[sourceSlot] = null;
        }
    }

    private void TryDropHeldItem(InventoryUiLayout layout, Point point, bool inPanel)
    {
        if (heldInventoryItem is null)
        {
            return;
        }

        if (!inPanel)
        {
            RestoreHeldItem();
            return;
        }

        if (TryResolveBackpackCell(layout, point, out var dropCell))
        {
            if (InventoryRules.CanPlaceAt(uiInventory, uiInventoryCatalog, heldInventoryItem.ItemCode, dropCell))
            {
                heldInventoryItem.Position = dropCell;
                uiInventory.BackpackItems.Add(heldInventoryItem);
                ClearHeldItem();
                return;
            }

            RestoreHeldItem();
            return;
        }

        if (TryResolveEquipSlot(layout, point, out var targetSlot))
        {
            if (TryDropHeldToEquip(targetSlot))
            {
                ClearHeldItem();
                return;
            }
        }

        RestoreHeldItem();
    }

    private void HandleInventoryRightClick(InventoryUiLayout layout, Point point)
    {
        if (TryResolveBackpackCell(layout, point, out var sourceCell))
        {
            if (InventoryRules.TryGetItemAtCell(uiInventory, uiInventoryCatalog, sourceCell, out var source, out _) &&
                uiInventoryCatalog.TryGet(source.ItemCode, out var def))
            {
                var slots = ResolveQuickEquipSlots(def);
                for (var i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    var tryEquip = InventoryOps.BackpackToEquip(uiInventory, uiInventoryCatalog, new BackpackToEquipRequest
                    {
                        BackpackCell = sourceCell,
                        TargetSlot = slot
                    });
                    if (tryEquip.Success)
                    {
                        return;
                    }
                }
            }

            return;
        }

        if (TryResolveEquipSlot(layout, point, out var equipSlot))
        {
            _ = InventoryOps.QuickUnequip(uiInventory, uiInventoryCatalog, equipSlot);
        }
    }

    private void RestoreHeldItem()
    {
        if (heldInventoryItem is null)
        {
            return;
        }

        if (heldFromEquipSlot is EquipSlot slot)
        {
            uiInventory.Equipment[slot] = heldInventoryItem;
        }
        else if (heldFromBackpackCell is GridCoord cell)
        {
            if (!InventoryRules.CanPlaceAt(uiInventory, uiInventoryCatalog, heldInventoryItem.ItemCode, cell))
            {
                if (!InventoryRules.TryFindFirstFit(uiInventory, uiInventoryCatalog, heldInventoryItem.ItemCode, out cell))
                {
                    cell = new GridCoord { X = 0, Y = 0 };
                }
            }

            heldInventoryItem.Position = cell;
            uiInventory.BackpackItems.Add(heldInventoryItem);
        }

        ClearHeldItem();
    }

    private bool TryDropHeldToEquip(EquipSlot targetSlot)
    {
        if (heldInventoryItem is null || !CanEquipHeldToSlot(targetSlot))
        {
            return false;
        }

        var tempCell = heldFromBackpackCell;
        if (!tempCell.HasValue || !InventoryRules.CanPlaceAt(uiInventory, uiInventoryCatalog, heldInventoryItem.ItemCode, tempCell.Value))
        {
            if (!InventoryRules.TryFindFirstFit(uiInventory, uiInventoryCatalog, heldInventoryItem.ItemCode, out var found))
            {
                return false;
            }

            tempCell = found;
        }

        heldInventoryItem.Position = tempCell.Value;
        uiInventory.BackpackItems.Add(heldInventoryItem);
        var result = InventoryOps.BackpackToEquip(uiInventory, uiInventoryCatalog, new BackpackToEquipRequest
        {
            BackpackCell = tempCell,
            TargetSlot = targetSlot
        });
        if (!result.Success)
        {
            uiInventory.BackpackItems.RemoveAll(x => string.Equals(x.InstanceId, heldInventoryItem.InstanceId, StringComparison.Ordinal));
            return false;
        }

        return true;
    }

    private bool CanEquipHeldToSlot(EquipSlot targetSlot)
    {
        if (heldInventoryItem is null || !uiInventoryCatalog.TryGet(heldInventoryItem.ItemCode, out var definition))
        {
            return false;
        }

        if (!InventoryRules.IsEquipmentSlotAllowed(definition, targetSlot))
        {
            return false;
        }

        if (InventoryRules.ViolatesEquipUniqueKey(uiInventory, uiInventoryCatalog, definition, targetSlot))
        {
            return false;
        }

        if (!uiInventory.Equipment.TryGetValue(targetSlot, out var existing) || existing is null)
        {
            return true;
        }

        // Equipping into an occupied slot requires displaced item to fit back in backpack.
        return InventoryRules.TryFindFirstFit(uiInventory, uiInventoryCatalog, existing.ItemCode, out _);
    }

    private void ClearHeldItem()
    {
        heldInventoryItem = null;
        heldFromEquipSlot = null;
        heldFromBackpackCell = null;
    }

    private bool TryResolveBackpackCell(InventoryUiLayout layout, Point point, out GridCoord cell)
    {
        cell = default;
        if (!layout.GridRect.Contains(point))
        {
            return false;
        }

        var step = InventoryCellSize + InventoryCellSpacing;
        var localX = point.X - layout.GridRect.X;
        var localY = point.Y - layout.GridRect.Y;
        var x = localX / step;
        var y = localY / step;
        if (x < 0 || y < 0 || x >= uiInventory.BackpackWidth || y >= uiInventory.BackpackHeight)
        {
            return false;
        }

        cell = new GridCoord { X = x, Y = y };
        return true;
    }

    private bool TryResolveEquipSlot(InventoryUiLayout layout, Point point, out EquipSlot slot)
    {
        foreach (var kv in layout.EquipRects)
        {
            if (kv.Value.Contains(point))
            {
                slot = kv.Key;
                return true;
            }
        }

        slot = default;
        return false;
    }

    private static DictionaryInventoryItemCatalog BuildUiInventoryCatalog()
    {
        return new DictionaryInventoryItemCatalog(new Dictionary<string, InventoryItemDefinition>(StringComparer.Ordinal)
        {
            ["item.sword.iron"] = new InventoryItemDefinition { ItemCode = "item.sword.iron", MaxStack = 1, GridWidth = 1, GridHeight = 3, EquipSlots = { EquipSlot.MainHand } },
            ["item.shield.ward"] = new InventoryItemDefinition { ItemCode = "item.shield.ward", MaxStack = 1, GridWidth = 2, GridHeight = 3, EquipSlots = { EquipSlot.OffHand } },
            ["item.helm.guard"] = new InventoryItemDefinition { ItemCode = "item.helm.guard", MaxStack = 1, GridWidth = 2, GridHeight = 2, EquipSlots = { EquipSlot.Head } },
            ["item.plate.bastion"] = new InventoryItemDefinition { ItemCode = "item.plate.bastion", MaxStack = 1, GridWidth = 2, GridHeight = 3, EquipSlots = { EquipSlot.Chest } },
            ["item.gloves.iron"] = new InventoryItemDefinition { ItemCode = "item.gloves.iron", MaxStack = 1, GridWidth = 2, GridHeight = 2, EquipSlots = { EquipSlot.Hands } },
            ["item.greaves.iron"] = new InventoryItemDefinition { ItemCode = "item.greaves.iron", MaxStack = 1, GridWidth = 2, GridHeight = 2, EquipSlots = { EquipSlot.Legs } },
            ["item.boots.iron"] = new InventoryItemDefinition { ItemCode = "item.boots.iron", MaxStack = 1, GridWidth = 2, GridHeight = 2, EquipSlots = { EquipSlot.Feet } },
            ["item.belt.guard"] = new InventoryItemDefinition { ItemCode = "item.belt.guard", MaxStack = 1, GridWidth = 2, GridHeight = 1, EquipSlots = { EquipSlot.Belt } },
            ["item.ring.guard"] = new InventoryItemDefinition { ItemCode = "item.ring.guard", MaxStack = 1, GridWidth = 1, GridHeight = 1, EquipSlots = { EquipSlot.Ring1, EquipSlot.Ring2 }, EquipUniqueKey = "ring.guard" },
            ["item.amulet.star"] = new InventoryItemDefinition { ItemCode = "item.amulet.star", MaxStack = 1, GridWidth = 1, GridHeight = 1, EquipSlots = { EquipSlot.Amulet } },
            ["item.relic.ward"] = new InventoryItemDefinition { ItemCode = "item.relic.ward", MaxStack = 1, GridWidth = 2, GridHeight = 2, EquipSlots = { EquipSlot.Relic } },
            ["item.potion.small"] = new InventoryItemDefinition { ItemCode = "item.potion.small", MaxStack = 20, GridWidth = 1, GridHeight = 1 }
        });
    }

    private void SeedUiInventory()
    {
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.sword.iron", Quantity = 1 });
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.shield.ward", Quantity = 1 });
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.helm.guard", Quantity = 1 });
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.plate.bastion", Quantity = 1 });
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.gloves.iron", Quantity = 1 });
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.greaves.iron", Quantity = 1 });
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.boots.iron", Quantity = 1 });
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.belt.guard", Quantity = 1 });
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.ring.guard", Quantity = 1 });
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.amulet.star", Quantity = 1 });
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.relic.ward", Quantity = 1 });
        _ = InventoryOps.AddToBackpack(uiInventory, uiInventoryCatalog, new InventoryItemStack { ItemCode = "item.potion.small", Quantity = 13 });
    }

    private static List<EquipSlot> ResolveQuickEquipSlots(InventoryItemDefinition definition)
    {
        if (!HasAccessoryTag(definition))
        {
            return definition.EquipSlots;
        }

        return new List<EquipSlot> { EquipSlot.Ring1, EquipSlot.Ring2, EquipSlot.Amulet, EquipSlot.Relic };
    }

    private static bool HasAccessoryTag(InventoryItemDefinition definition)
    {
        for (var i = 0; i < definition.EquipSlots.Count; i++)
        {
            var slot = definition.EquipSlots[i];
            if (slot == EquipSlot.Ring1 || slot == EquipSlot.Ring2 || slot == EquipSlot.Amulet || slot == EquipSlot.Relic)
            {
                return true;
            }
        }

        return false;
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
                var archetype = latestEnemyArchetypeByEntity.TryGetValue(pair.Key, out var id) && !string.IsNullOrWhiteSpace(id)
                    ? id
                    : "enemy.unknown";
                return $"{archetype} ({pair.Key}): threat={threat} forced={forced}";
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
        if (inventoryPanelOpen && !pauseMenuOpen)
        {
            UpdateInventoryUi(mouse);
        }

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

    private static bool WasReleased(MouseState current, MouseState previous, bool left)
    {
        return left
            ? current.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed
            : current.RightButton == ButtonState.Released && previous.RightButton == ButtonState.Pressed;
    }

    private bool IsPointerOverInventoryModal(MouseState mouse)
    {
        if (!inventoryPanelOpen || pauseMenuOpen || screen != UiScreen.InGame)
        {
            return false;
        }

        var layout = BuildInventoryLayout();
        return layout.Panel.Contains(mouse.Position);
    }

    private InputActionFlags ReadActionFlags(KeyboardState keyboard, MouseState mouse, bool suppressMouseCombat)
    {
        var flags = InputActionFlags.None;
        if (!suppressMouseCombat && mouse.LeftButton == ButtonState.Pressed)
        {
            flags |= InputActionFlags.FastAttackHold;
        }

        if (!suppressMouseCombat && mouse.RightButton == ButtonState.Pressed)
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
            flags |= InputActionFlags.Interact;
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
        latestEnemyArchetypeByEntity.Clear();
        latestEnemyAggroTarget.Clear();
        latestEnemyAggroThreat.Clear();
        latestEnemyForcedTicks.Clear();
        latestEnemyPrimaryStatusStacks.Clear();
        lootCurrencyById.Clear();
        latestZones.Clear();
        latestLinks.Clear();
        latestWorldObjects.Clear();
        latestHazards.Clear();
        latestActiveObjectiveTargets.Clear();
        questLogActs.Clear();
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
        selectedQuestActIndex = 0;
        selectedQuestZoneIndex = 0;
        selectedQuestIndex = 0;

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

    private readonly record struct InventoryUiLayout(
        Rectangle Panel,
        Rectangle EquipRegion,
        Rectangle GridRect,
        Dictionary<EquipSlot, Rectangle> EquipRects);

    private readonly record struct UiButton(Rectangle Bounds, string Label, Action OnClick, bool Enabled);
    private readonly record struct UiTextField(string Id, Rectangle Bounds);

}
