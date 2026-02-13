using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Armament.Client.MonoGame.Animation;

internal sealed class AtlasAnimationRuntime : IDisposable
{
    private readonly Dictionary<string, AtlasClip> clips;
    private readonly Dictionary<byte, string> castClipBySlotCode;
    private readonly Dictionary<LocomotionKey, string> locomotionClipByKey;

    public AtlasAnimationRuntime(string baseClassId, string specId, IReadOnlyDictionary<string, AtlasClip> clips, ClipMapSelection clipMap)
    {
        BaseClassId = baseClassId;
        SpecId = specId;
        this.clips = new Dictionary<string, AtlasClip>(clips, StringComparer.OrdinalIgnoreCase);

        IdleClipId = ResolveClipId(clipMap.IdleClipId, () => SelectFirst("idle"));
        MoveClipId = ResolveClipId(clipMap.MoveClipId, () => SelectFirst("run", "move", "strafe"));
        BlockLoopClipId = ResolveClipId(clipMap.BlockLoopClipId, () => SelectFirst("block", "guard"));
        HeavyClipId = ResolveClipId(clipMap.HeavyClipId, () => SelectFirst("heavy", "r3", "attack_r3"));
        TurnLeftClipId = ResolveOptionalClipId(clipMap.TurnLeftClipId, () => SelectFirst("turn01_left", "turn_left"));
        TurnRightClipId = ResolveOptionalClipId(clipMap.TurnRightClipId, () => SelectFirst("turn01_right", "turn_right"));

        locomotionClipByKey = new Dictionary<LocomotionKey, string>();
        RegisterLocomotion(LocomotionKey.RunForward, clipMap.RunForwardClipId, "run01_forward");
        RegisterLocomotion(LocomotionKey.RunBackward, clipMap.RunBackwardClipId, "run01_backward");
        RegisterLocomotion(LocomotionKey.RunLeft, clipMap.RunLeftClipId, "run01_left");
        RegisterLocomotion(LocomotionKey.RunRight, clipMap.RunRightClipId, "run01_right");
        RegisterLocomotion(LocomotionKey.RunForwardLeft, clipMap.RunForwardLeftClipId, "run01_forwardleft");
        RegisterLocomotion(LocomotionKey.RunForwardRight, clipMap.RunForwardRightClipId, "run01_forwardright");
        RegisterLocomotion(LocomotionKey.RunBackwardLeft, clipMap.RunBackwardLeftClipId, "run01_backwardleft");
        RegisterLocomotion(LocomotionKey.RunBackwardRight, clipMap.RunBackwardRightClipId, "run01_backwardright");
        RegisterLocomotion(LocomotionKey.StrafeLeft, clipMap.StrafeLeftClipId, "straferun01_left");
        RegisterLocomotion(LocomotionKey.StrafeRight, clipMap.StrafeRightClipId, "straferun01_right");
        RegisterLocomotion(LocomotionKey.StrafeForwardLeft, clipMap.StrafeForwardLeftClipId, "straferun01_forwardleft");
        RegisterLocomotion(LocomotionKey.StrafeForwardRight, clipMap.StrafeForwardRightClipId, "straferun01_forwardright");
        RegisterLocomotion(LocomotionKey.StrafeBackwardLeft, clipMap.StrafeBackwardLeftClipId, "straferun01_backwardleft");
        RegisterLocomotion(LocomotionKey.StrafeBackwardRight, clipMap.StrafeBackwardRightClipId, "straferun01_backwardright");

        var chain = new List<string>();
        if (clipMap.FastChainClipIds.Count > 0)
        {
            for (var i = 0; i < clipMap.FastChainClipIds.Count; i++)
            {
                var resolved = ResolveClipId(clipMap.FastChainClipIds[i], () => string.Empty);
                if (!string.IsNullOrWhiteSpace(resolved) && !chain.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                {
                    chain.Add(resolved);
                }
            }
        }

        if (chain.Count == 0)
        {
            AddIfPresent(chain, SelectFirst("attackshield01"));
            AddIfPresent(chain, SelectFirst("unarmed", "attack-r1", "attack_r1"));
            AddIfPresent(chain, SelectFirst("attackshield02"));
            AddIfPresent(chain, SelectFirst("attack", "fast"));
        }

        FastChainClipIds = chain;

        castClipBySlotCode = new Dictionary<byte, string>();
        foreach (var pair in clipMap.CastClipBySlotLabel)
        {
            if (!TryResolveSlotCode(pair.Key, out var slotCode))
            {
                continue;
            }

            var resolved = ResolveClipId(pair.Value, () => string.Empty);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                castClipBySlotCode[slotCode] = resolved;
            }
        }
    }

    public string BaseClassId { get; }
    public string SpecId { get; }
    public string IdleClipId { get; }
    public string MoveClipId { get; }
    public string BlockLoopClipId { get; }
    public string HeavyClipId { get; }
    public string TurnLeftClipId { get; }
    public string TurnRightClipId { get; }
    public IReadOnlyList<string> FastChainClipIds { get; }

    public bool TryGet(string clipId, out AtlasClip clip) => clips.TryGetValue(clipId, out clip!);

    public bool TryGetCastClip(byte slotCode, out string clipId)
    {
        return castClipBySlotCode.TryGetValue(slotCode, out clipId!);
    }

    private string ResolveClipId(string requested, Func<string> fallback)
    {
        if (!string.IsNullOrWhiteSpace(requested) && clips.ContainsKey(requested))
        {
            return requested;
        }

        var candidate = fallback();
        if (!string.IsNullOrWhiteSpace(candidate) && clips.ContainsKey(candidate))
        {
            return candidate;
        }

        return clips.Keys.FirstOrDefault() ?? string.Empty;
    }

    private string ResolveOptionalClipId(string requested, Func<string> fallback)
    {
        if (!string.IsNullOrWhiteSpace(requested) && clips.ContainsKey(requested))
        {
            return requested;
        }

        var candidate = fallback();
        if (!string.IsNullOrWhiteSpace(candidate) && clips.ContainsKey(candidate))
        {
            return candidate;
        }

        return string.Empty;
    }

    private string SelectFirst(params string[] terms)
    {
        foreach (var kv in clips)
        {
            var id = kv.Key;
            var ok = true;
            for (var i = 0; i < terms.Length; i++)
            {
                if (!id.Contains(terms[i], StringComparison.OrdinalIgnoreCase))
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                return kv.Key;
            }
        }

        return string.Empty;
    }

    private static bool TryResolveSlotCode(string slotLabel, out byte slotCode)
    {
        slotCode = slotLabel.Trim().ToUpperInvariant() switch
        {
            "LMB" => 0,
            "RMB" => 1,
            "SHIFT" => 2,
            "E" => 3,
            "R" => 4,
            "Q" => 5,
            "T" => 6,
            "1" => 7,
            "2" => 8,
            "3" => 9,
            "4" => 10,
            _ => byte.MaxValue
        };

        return slotCode != byte.MaxValue;
    }

    private static void AddIfPresent(List<string> list, string clipId)
    {
        if (!string.IsNullOrWhiteSpace(clipId) && !list.Contains(clipId, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(clipId);
        }
    }

    private void RegisterLocomotion(LocomotionKey key, string requestedClipId, params string[] fallbackTerms)
    {
        var resolved = ResolveClipId(requestedClipId, () => SelectFirst(fallbackTerms));
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            locomotionClipByKey[key] = resolved;
        }
    }

    public string ResolveLocomotionClipId(LocomotionKey key, bool preferStrafe)
    {
        if (preferStrafe)
        {
            var strafeKey = key switch
            {
                LocomotionKey.RunLeft => LocomotionKey.StrafeLeft,
                LocomotionKey.RunRight => LocomotionKey.StrafeRight,
                LocomotionKey.RunForwardLeft => LocomotionKey.StrafeForwardLeft,
                LocomotionKey.RunForwardRight => LocomotionKey.StrafeForwardRight,
                LocomotionKey.RunBackwardLeft => LocomotionKey.StrafeBackwardLeft,
                LocomotionKey.RunBackwardRight => LocomotionKey.StrafeBackwardRight,
                _ => key
            };

            if (locomotionClipByKey.TryGetValue(strafeKey, out var strafe) && !string.IsNullOrWhiteSpace(strafe))
            {
                return strafe;
            }
        }

        if (locomotionClipByKey.TryGetValue(key, out var clip) && !string.IsNullOrWhiteSpace(clip))
        {
            return clip;
        }

        return MoveClipId;
    }

    public void Dispose()
    {
        var disposed = new HashSet<Texture2D>();
        foreach (var clip in clips.Values)
        {
            if (disposed.Add(clip.Atlas))
            {
                clip.Atlas.Dispose();
            }
        }
    }
}

internal sealed class AtlasClip
{
    public required string ClipId { get; init; }
    public required Texture2D Atlas { get; init; }
    public required int Directions { get; init; }
    public required int PixelsPerUnit { get; init; }
    public required Dictionary<int, AtlasFrame[]> FramesByDirection { get; init; }

    public bool TryGetFrames(int direction, out AtlasFrame[] frames)
    {
        if (FramesByDirection.TryGetValue(direction, out frames!))
        {
            return true;
        }

        if (FramesByDirection.TryGetValue(0, out frames!))
        {
            return true;
        }

        frames = Array.Empty<AtlasFrame>();
        return false;
    }
}

internal readonly record struct AtlasFrame(Rectangle Source, Vector2 OriginPixels, float DurationSeconds);

internal enum LocomotionKey
{
    RunForward = 0,
    RunForwardRight = 1,
    RunRight = 2,
    RunBackwardRight = 3,
    RunBackward = 4,
    RunBackwardLeft = 5,
    RunLeft = 6,
    RunForwardLeft = 7,
    StrafeLeft = 8,
    StrafeRight = 9,
    StrafeForwardLeft = 10,
    StrafeForwardRight = 11,
    StrafeBackwardLeft = 12,
    StrafeBackwardRight = 13
}

internal sealed class LocalAtlasAnimator
{
    private const float ComboGraceSeconds = 0.42f;
    private const byte LmbSlotCode = 0;

    private readonly AtlasAnimationRuntime runtime;
    private string currentClipId = string.Empty;
    private int currentFrameIndex;
    private float frameRemainingSeconds;
    private int chainIndex;
    private string castForcedClipId = string.Empty;
    private string activeFastAttackClipId = string.Empty;
    private bool fastAttackPlaying;
    private bool localFastAttackIntentPending;
    private bool serverAuthorizedFastAttackPending;
    private bool castActionPlaying;
    private int lockedActionDirection = -1;
    private float comboGraceRemainingSeconds;
    private int lastFacingDirection;
    private int lastFrameCount;

    public LocalAtlasAnimator(AtlasAnimationRuntime runtime)
    {
        this.runtime = runtime;
    }

    public void ResetState()
    {
        currentClipId = string.Empty;
        currentFrameIndex = 0;
        frameRemainingSeconds = 0f;
        chainIndex = 0;
        castForcedClipId = string.Empty;
        activeFastAttackClipId = string.Empty;
        fastAttackPlaying = false;
        localFastAttackIntentPending = false;
        serverAuthorizedFastAttackPending = false;
        castActionPlaying = false;
        lockedActionDirection = -1;
        comboGraceRemainingSeconds = 0f;
        lastFacingDirection = 0;
        lastFrameCount = 0;
    }

    public void NotifyAuthoritativeCast(byte slotCode, byte resultCode)
    {
        if (resultCode != 1)
        {
            return;
        }

        if (runtime.TryGetCastClip(slotCode, out var clipId) && !string.IsNullOrWhiteSpace(clipId))
        {
            castForcedClipId = clipId;
        }

        if (slotCode == LmbSlotCode)
        {
            serverAuthorizedFastAttackPending = true;
        }
    }

    public void NotifyLocalFastAttackIntent()
    {
        localFastAttackIntentPending = true;
    }

    public bool TryResolveFrame(float dt, int direction, Vector2 moveVector, bool moving, bool blockHold, bool fastHold, bool heavyHold, out AtlasClip clip, out AtlasFrame frame)
    {
        clip = default!;
        TickFastAttackState(dt, fastHold);

        var selectedClipId = ResolveClip(direction, moveVector, moving, blockHold, fastHold, heavyHold);
        if (string.IsNullOrWhiteSpace(selectedClipId) || !runtime.TryGet(selectedClipId, out clip))
        {
            frame = default;
            return false;
        }

        var playbackDirection = (fastAttackPlaying || castActionPlaying) && lockedActionDirection >= 0
            ? lockedActionDirection
            : direction;

        if (!clip.TryGetFrames(playbackDirection, out var frames) || frames.Length == 0)
        {
            frame = default;
            return false;
        }

        var clipChanged = !string.Equals(currentClipId, clip.ClipId, StringComparison.OrdinalIgnoreCase);
        if (clipChanged)
        {
            var oldIndex = currentFrameIndex;
            var oldCount = Math.Max(1, lastFrameCount);
            currentClipId = clip.ClipId;

            // Keep locomotion transitions smooth, but make combat/cast transitions snappy.
            var preservePhase = ShouldPreservePhaseForClipSwitch(selectedClipId, moving, blockHold, fastHold, heavyHold);
            if (!preservePhase || frames.Length <= 1)
            {
                currentFrameIndex = 0;
            }
            else
            {
                var t = MathHelper.Clamp((float)oldIndex / Math.Max(1, oldCount - 1), 0f, 1f);
                currentFrameIndex = Math.Clamp((int)MathF.Round(t * (frames.Length - 1)), 0, frames.Length - 1);
            }
            frameRemainingSeconds = frames[0].DurationSeconds;
        }
        else
        {
            frameRemainingSeconds -= MathF.Max(0f, dt);
            while (frameRemainingSeconds <= 0f)
            {
                var isCastForced = !string.IsNullOrWhiteSpace(castForcedClipId) && string.Equals(selectedClipId, castForcedClipId, StringComparison.OrdinalIgnoreCase);
                var nextIndex = currentFrameIndex + 1;
                if (nextIndex >= frames.Length)
                {
                    if (fastAttackPlaying &&
                        !string.IsNullOrWhiteSpace(activeFastAttackClipId) &&
                        string.Equals(selectedClipId, activeFastAttackClipId, StringComparison.OrdinalIgnoreCase))
                    {
                        fastAttackPlaying = false;
                        activeFastAttackClipId = string.Empty;
                        if (!castActionPlaying)
                        {
                            lockedActionDirection = -1;
                        }
                        comboGraceRemainingSeconds = ComboGraceSeconds;

                        nextIndex = Math.Max(0, frames.Length - 1);
                        currentFrameIndex = nextIndex;
                        frameRemainingSeconds = 0.001f;
                        break;
                    }

                    if (isCastForced)
                    {
                        castForcedClipId = string.Empty;
                        castActionPlaying = false;
                        if (!fastAttackPlaying)
                        {
                            lockedActionDirection = -1;
                        }
                        // drop out; base state clip will resolve on next tick
                        nextIndex = 0;
                    }
                    else
                    {
                        nextIndex = 0;
                    }
                }

                currentFrameIndex = nextIndex;
                frameRemainingSeconds += frames[currentFrameIndex].DurationSeconds;
            }
        }

        frame = frames[currentFrameIndex];
        lastFacingDirection = direction;
        lastFrameCount = frames.Length;
        return true;
    }

    private void TickFastAttackState(float dt, bool fastHold)
    {
        comboGraceRemainingSeconds = MathF.Max(0f, comboGraceRemainingSeconds - MathF.Max(0f, dt));
        if (!fastAttackPlaying && comboGraceRemainingSeconds <= 0f)
        {
            chainIndex = 0;
        }
    }

    private bool ShouldPreservePhaseForClipSwitch(string selectedClipId, bool moving, bool blockHold, bool fastHold, bool heavyHold)
    {
        if (!moving)
        {
            return false;
        }

        if (blockHold || fastHold || heavyHold)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(castForcedClipId) &&
            string.Equals(selectedClipId, castForcedClipId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private string ResolveClip(int direction, Vector2 moveVector, bool moving, bool blockHold, bool fastHold, bool heavyHold)
    {
        if (!string.IsNullOrWhiteSpace(castForcedClipId))
        {
            castActionPlaying = true;
            if (lockedActionDirection < 0)
            {
                lockedActionDirection = direction;
            }
            return castForcedClipId;
        }

        if (fastAttackPlaying && !string.IsNullOrWhiteSpace(activeFastAttackClipId))
        {
            return activeFastAttackClipId;
        }

        if ((localFastAttackIntentPending || serverAuthorizedFastAttackPending) && TryBeginFastAttack(direction))
        {
            return activeFastAttackClipId;
        }

        if (blockHold && !string.IsNullOrWhiteSpace(runtime.BlockLoopClipId))
        {
            castActionPlaying = false;
            return runtime.BlockLoopClipId;
        }

        if (heavyHold && !string.IsNullOrWhiteSpace(runtime.HeavyClipId))
        {
            castActionPlaying = false;
            return runtime.HeavyClipId;
        }

        if (fastHold && runtime.FastChainClipIds.Count > 0)
        {
            var clip = runtime.FastChainClipIds[chainIndex % runtime.FastChainClipIds.Count];
            chainIndex++;
            return clip;
        }

        if (moving)
        {
            castActionPlaying = false;
            var locomotionKey = ResolveLocomotionKey(direction, moveVector);
            var preferStrafe = blockHold || fastHold || heavyHold;
            return runtime.ResolveLocomotionClipId(locomotionKey, preferStrafe);
        }

        castActionPlaying = false;
        return runtime.IdleClipId;
    }

    private bool TryBeginFastAttack(int direction)
    {
        if (runtime.FastChainClipIds.Count == 0)
        {
            serverAuthorizedFastAttackPending = false;
            return false;
        }

        activeFastAttackClipId = runtime.FastChainClipIds[chainIndex % runtime.FastChainClipIds.Count];
        chainIndex++;
        fastAttackPlaying = true;
        localFastAttackIntentPending = false;
        serverAuthorizedFastAttackPending = false;
        castActionPlaying = false;
        if (lockedActionDirection < 0)
        {
            lockedActionDirection = direction;
        }
        comboGraceRemainingSeconds = ComboGraceSeconds;
        return !string.IsNullOrWhiteSpace(activeFastAttackClipId);
    }

    private static LocomotionKey ResolveLocomotionKey(int facingDirection, Vector2 moveVector)
    {
        if (moveVector.LengthSquared() <= 0.0001f)
        {
            return LocomotionKey.RunForward;
        }

        var move = Vector2.Normalize(moveVector);
        var forward = DirectionToVector(facingDirection);
        var right = new Vector2(forward.Y, -forward.X);
        var dotF = Vector2.Dot(move, forward);
        var dotR = Vector2.Dot(move, right);
        var angle = MathF.Atan2(dotR, dotF);
        var normalized = (angle + MathF.PI * 2f) % (MathF.PI * 2f);
        var sector = (int)MathF.Round(normalized / (MathF.PI / 4f)) % 8;

        return sector switch
        {
            0 => LocomotionKey.RunForward,
            1 => LocomotionKey.RunForwardRight,
            2 => LocomotionKey.RunRight,
            3 => LocomotionKey.RunBackwardRight,
            4 => LocomotionKey.RunBackward,
            5 => LocomotionKey.RunBackwardLeft,
            6 => LocomotionKey.RunLeft,
            7 => LocomotionKey.RunForwardLeft,
            _ => LocomotionKey.RunForward
        };
    }

    private static Vector2 DirectionToVector(int direction)
    {
        var idx = ((direction % 8) + 8) % 8;
        var angleDegrees = 45f - idx * 45f;
        var rad = angleDegrees * (MathF.PI / 180f);
        return new Vector2(MathF.Cos(rad), MathF.Sin(rad));
    }
}

internal static class AtlasAnimationLoader
{
    // Final runtime guard against tiny source-rect under-captures from authored data.
    private const int RuntimeSourceOverscanPixels = 4;
    // 0=NE,1=E,2=SE,3=S,4=SW,5=W,6=NW,7=N (project convention).
    private const int PivotReferenceDirection = 2;
    private const float DirectionTemporalSmoothY = 0.35f;

    public static AtlasAnimationRuntime? TryLoad(GraphicsDevice graphicsDevice, string repoRoot, string baseClassId, string specId)
    {
        var classDir = Path.Combine(repoRoot, "content", "animations", baseClassId.ToLowerInvariant());
        if (!Directory.Exists(classDir))
        {
            return null;
        }

        var jsonFiles = Directory.GetFiles(classDir, "*.json", SearchOption.AllDirectories)
            .Where(path =>
                !path.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("clipmap.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (jsonFiles.Length == 0)
        {
            return null;
        }

        var clips = new Dictionary<string, AtlasClip>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < jsonFiles.Length; i++)
        {
            try
            {
                var jsonPath = jsonFiles[i];
                var json = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var clipId = ReadString(root, "clipId");
                if (string.IsNullOrWhiteSpace(clipId))
                {
                    clipId = Path.GetFileNameWithoutExtension(jsonPath);
                }

                var directions = ReadInt(root, "directions", 8);
                var fps = ReadFloat(root, "fps", 24f);
                var ppu = ReadInt(root, "pixelsPerUnit", 64);
                if (directions <= 0)
                {
                    directions = 8;
                }

                var atlasPath = ResolveAtlasPath(root, jsonPath);
                if (string.IsNullOrWhiteSpace(atlasPath) || !File.Exists(atlasPath))
                {
                    continue;
                }

                using var stream = File.OpenRead(atlasPath);
                var texture = Texture2D.FromStream(graphicsDevice, stream);

                if (!root.TryGetProperty("frames", out var framesNode) || framesNode.ValueKind != JsonValueKind.Array)
                {
                    texture.Dispose();
                    continue;
                }

                var grouped = new Dictionary<int, List<RawAtlasFrame>>();
                var sourceIndex = 0;
                foreach (var frameNode in framesNode.EnumerateArray())
                {
                    var dir = ReadInt(frameNode, "direction", 0);
                    var frameNumber = ReadInt(frameNode, "frame", sourceIndex);
                    var x = ReadInt(frameNode, "x", 0);
                    var y = ReadInt(frameNode, "y", 0);
                    var w = ReadInt(frameNode, "w", 0);
                    var h = ReadInt(frameNode, "h", 0);
                    if (w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > texture.Width || y + h > texture.Height)
                    {
                        sourceIndex++;
                        continue;
                    }

                    var frameTime = ReadFloat(frameNode, "time", -1f);
                    var pivotX = ReadInt(frameNode, "pivotX", w / 2);
                    var pivotY = ReadInt(frameNode, "pivotY", h / 2);
                    var sourceRect = new Rectangle(x, y, w, h);
                    var origin = new Vector2(pivotX, pivotY);
                    ApplyRuntimeOverscan(texture.Width, texture.Height, ref sourceRect, ref origin, RuntimeSourceOverscanPixels);
                    var frame = new RawAtlasFrame
                    {
                        Direction = dir,
                        FrameNumber = frameNumber,
                        SourceIndex = sourceIndex,
                        Source = sourceRect,
                        Origin = origin,
                        TimeValue = frameTime
                    };

                    if (!grouped.TryGetValue(dir, out var list))
                    {
                        list = new List<RawAtlasFrame>();
                        grouped[dir] = list;
                    }

                    list.Add(frame);
                    sourceIndex++;
                }

                if (grouped.Count == 0)
                {
                    texture.Dispose();
                    continue;
                }

                grouped = StabilizeRawPivots(grouped);

                var map = new Dictionary<int, AtlasFrame[]>();
                foreach (var pair in grouped)
                {
                    map[pair.Key] = BuildAtlasFramesForDirection(pair.Value, fps);
                }

                if (clips.TryGetValue(clipId, out var existing))
                {
                    existing.Atlas.Dispose();
                    clips.Remove(clipId);
                }

                clips[clipId] = new AtlasClip
                {
                    ClipId = clipId,
                    Atlas = texture,
                    Directions = directions,
                    PixelsPerUnit = Math.Max(1, ppu),
                    FramesByDirection = map
                };
            }
            catch
            {
            }
        }

        if (clips.Count == 0)
        {
            return null;
        }

        var clipMap = LoadClipMap(repoRoot, baseClassId, specId);
        return new AtlasAnimationRuntime(baseClassId, specId, clips, clipMap);
    }

    private static ClipMapSelection LoadClipMap(string repoRoot, string baseClassId, string specId)
    {
        var path = Path.Combine(repoRoot, "content", "animations", "clipmaps", $"{baseClassId.ToLowerInvariant()}.json");
        if (!File.Exists(path))
        {
            return ClipMapSelection.Empty;
        }

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<ClipMapFile>(json);
            if (parsed is null)
            {
                return ClipMapSelection.Empty;
            }

            var selection = ClipMapSelection.From(parsed.Default);
            if (parsed.Specs is not null && parsed.Specs.TryGetValue(specId, out var specDef) && specDef is not null)
            {
                selection.Apply(specDef);
            }

            return selection;
        }
        catch
        {
            return ClipMapSelection.Empty;
        }
    }

    private static string ResolveAtlasPath(JsonElement root, string jsonPath)
    {
        var atlasRaw = ReadString(root, "atlasTexture");
        if (string.IsNullOrWhiteSpace(atlasRaw))
        {
            atlasRaw = ReadString(root, "atlas");
        }

        if (string.IsNullOrWhiteSpace(atlasRaw))
        {
            var sameName = Path.ChangeExtension(jsonPath, ".png");
            if (File.Exists(sameName))
            {
                return sameName;
            }

            var dir = Path.GetDirectoryName(jsonPath) ?? string.Empty;
            var pngs = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly);
            return pngs.FirstOrDefault() ?? string.Empty;
        }

        if (Path.IsPathRooted(atlasRaw))
        {
            return atlasRaw;
        }

        var baseDir = Path.GetDirectoryName(jsonPath) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(baseDir, atlasRaw));
    }

    private static string ReadString(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return prop.GetString() ?? string.Empty;
    }

    private static int ReadInt(JsonElement node, string name, int fallback)
    {
        if (!node.TryGetProperty(name, out var prop))
        {
            return fallback;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetInt32(out var v) => v,
            JsonValueKind.String when int.TryParse(prop.GetString(), out var v) => v,
            _ => fallback
        };
    }

    private static float ReadFloat(JsonElement node, string name, float fallback)
    {
        if (!node.TryGetProperty(name, out var prop))
        {
            return fallback;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetSingle(out var v) => v,
            JsonValueKind.String when float.TryParse(prop.GetString(), out var v) => v,
            _ => fallback
        };
    }

    private static AtlasFrame[] BuildAtlasFramesForDirection(List<RawAtlasFrame> rawFrames, float fps)
    {
        if (rawFrames.Count == 0)
        {
            return Array.Empty<AtlasFrame>();
        }

        rawFrames.Sort((a, b) =>
        {
            var byFrame = a.FrameNumber.CompareTo(b.FrameNumber);
            return byFrame != 0 ? byFrame : a.SourceIndex.CompareTo(b.SourceIndex);
        });

        var defaultDuration = 1f / MathF.Max(1f, fps);
        var useCumulative = LooksLikeCumulativeTimeline(rawFrames, defaultDuration);
        var output = new AtlasFrame[rawFrames.Count];
        var previousTime = 0f;
        for (var i = 0; i < rawFrames.Count; i++)
        {
            var raw = rawFrames[i];
            var duration = defaultDuration;
            if (raw.TimeValue > 0f)
            {
                if (useCumulative)
                {
                    if (i == 0)
                    {
                        duration = defaultDuration;
                    }
                    else
                    {
                        duration = raw.TimeValue - previousTime;
                        if (duration <= 0f)
                        {
                            duration = defaultDuration;
                        }
                    }
                }
                else
                {
                    duration = raw.TimeValue;
                }
            }

            output[i] = new AtlasFrame(raw.Source, raw.Origin, MathF.Max(0.001f, duration));
            if (raw.TimeValue > 0f)
            {
                previousTime = raw.TimeValue;
            }
        }

        return output;
    }

    private static Dictionary<int, List<RawAtlasFrame>> StabilizeRawPivots(Dictionary<int, List<RawAtlasFrame>> grouped)
    {
        if (grouped.Count == 0)
        {
            return grouped;
        }

        foreach (var pair in grouped)
        {
            pair.Value.Sort((a, b) =>
            {
                var byFrame = a.FrameNumber.CompareTo(b.FrameNumber);
                return byFrame != 0 ? byFrame : a.SourceIndex.CompareTo(b.SourceIndex);
            });
        }

        var medianXByDir = new Dictionary<int, int>();
        var medianYByDir = new Dictionary<int, int>();
        foreach (var pair in grouped)
        {
            var xs = new List<int>(pair.Value.Count);
            var ys = new List<int>(pair.Value.Count);
            for (var i = 0; i < pair.Value.Count; i++)
            {
                xs.Add((int)MathF.Round(pair.Value[i].Origin.X));
                ys.Add((int)MathF.Round(pair.Value[i].Origin.Y));
            }

            medianXByDir[pair.Key] = Median(xs);
            medianYByDir[pair.Key] = Median(ys);
        }

        var referenceByFrame = new Dictionary<int, (int x, int y)>();
        if (grouped.TryGetValue(PivotReferenceDirection, out var referenceFrames))
        {
            for (var i = 0; i < referenceFrames.Count; i++)
            {
                var rf = referenceFrames[i];
                referenceByFrame[rf.FrameNumber] = ((int)MathF.Round(rf.Origin.X), (int)MathF.Round(rf.Origin.Y));
            }
        }

        var stabilized = new Dictionary<int, List<RawAtlasFrame>>();
        foreach (var pair in grouped)
        {
            var dir = pair.Key;
            var list = pair.Value;
            var outList = new List<RawAtlasFrame>(list.Count);
            int? lastY = null;
            for (var i = 0; i < list.Count; i++)
            {
                var src = list[i];
                var targetX = medianXByDir.TryGetValue(dir, out var medianX) ? medianX : (int)MathF.Round(src.Origin.X);
                var targetY = medianYByDir.TryGetValue(dir, out var medianY) ? medianY : (int)MathF.Round(src.Origin.Y);

                // Cross-direction anchor consistency: adopt SE row frame pivot when available.
                if (referenceByFrame.TryGetValue(src.FrameNumber, out var referencePivot))
                {
                    targetX = referencePivot.x;
                    targetY = referencePivot.y;
                }

                // Intra-direction temporal smoothing to remove frame-to-frame bounce.
                if (lastY.HasValue)
                {
                    targetY = (int)MathF.Round(MathHelper.Lerp(targetY, lastY.Value, DirectionTemporalSmoothY));
                }

                targetX = Math.Clamp(targetX, 0, Math.Max(0, src.Source.Width - 1));
                targetY = Math.Clamp(targetY, 0, Math.Max(0, src.Source.Height - 1));
                lastY = targetY;

                outList.Add(new RawAtlasFrame
                {
                    Direction = src.Direction,
                    FrameNumber = src.FrameNumber,
                    SourceIndex = src.SourceIndex,
                    Source = src.Source,
                    Origin = new Vector2(targetX, targetY),
                    TimeValue = src.TimeValue
                });
            }

            stabilized[dir] = outList;
        }

        return stabilized;
    }

    private static int Median(List<int> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        var mid = values.Count / 2;
        if ((values.Count & 1) == 1)
        {
            return values[mid];
        }

        return (int)MathF.Round((values[mid - 1] + values[mid]) * 0.5f);
    }

    private static bool LooksLikeCumulativeTimeline(List<RawAtlasFrame> frames, float defaultDuration)
    {
        if (frames.Count < 3)
        {
            return false;
        }

        var positive = 0;
        var monotonic = 0;
        var prev = -1f;
        var max = 0f;

        for (var i = 0; i < frames.Count; i++)
        {
            var t = frames[i].TimeValue;
            if (t <= 0f)
            {
                continue;
            }

            positive++;
            if (prev >= 0f && t >= prev)
            {
                monotonic++;
            }

            prev = t;
            if (t > max)
            {
                max = t;
            }
        }

        if (positive < 3)
        {
            return false;
        }

        var mostlyMonotonic = monotonic >= positive - 2;
        var stretchedRange = max > defaultDuration * 2.5f;
        return mostlyMonotonic && stretchedRange;
    }

    private static void ApplyRuntimeOverscan(int textureWidth, int textureHeight, ref Rectangle source, ref Vector2 origin, int overscanPixels)
    {
        var pad = Math.Clamp(overscanPixels, 0, 8);
        if (pad <= 0)
        {
            return;
        }

        var oldX = source.X;
        var oldY = source.Y;
        var oldRight = source.Right;
        var oldBottom = source.Bottom;

        var newX = Math.Max(0, oldX - pad);
        var newY = Math.Max(0, oldY - pad);
        var newRight = Math.Min(textureWidth, oldRight + pad);
        var newBottom = Math.Min(textureHeight, oldBottom + pad);

        var newW = Math.Max(1, newRight - newX);
        var newH = Math.Max(1, newBottom - newY);
        source = new Rectangle(newX, newY, newW, newH);

        var dx = oldX - newX;
        var dy = oldY - newY;
        origin = new Vector2(origin.X + dx, origin.Y + dy);
    }
}

internal sealed class RawAtlasFrame
{
    public int Direction { get; init; }
    public int FrameNumber { get; init; }
    public int SourceIndex { get; init; }
    public Rectangle Source { get; init; }
    public Vector2 Origin { get; init; }
    public float TimeValue { get; init; }
}

internal sealed class ClipMapFile
{
    public ClipMapDefinition? Default { get; set; }
    public Dictionary<string, ClipMapDefinition?>? Specs { get; set; }
}

internal sealed class ClipMapDefinition
{
    public string? IdleClipId { get; set; }
    public string? MoveClipId { get; set; }
    public string? RunForwardClipId { get; set; }
    public string? RunBackwardClipId { get; set; }
    public string? RunLeftClipId { get; set; }
    public string? RunRightClipId { get; set; }
    public string? RunForwardLeftClipId { get; set; }
    public string? RunForwardRightClipId { get; set; }
    public string? RunBackwardLeftClipId { get; set; }
    public string? RunBackwardRightClipId { get; set; }
    public string? StrafeLeftClipId { get; set; }
    public string? StrafeRightClipId { get; set; }
    public string? StrafeForwardLeftClipId { get; set; }
    public string? StrafeForwardRightClipId { get; set; }
    public string? StrafeBackwardLeftClipId { get; set; }
    public string? StrafeBackwardRightClipId { get; set; }
    public string? TurnLeftClipId { get; set; }
    public string? TurnRightClipId { get; set; }
    public string? BlockLoopClipId { get; set; }
    public string? HeavyClipId { get; set; }
    public List<string>? FastChainClipIds { get; set; }
    public Dictionary<string, string>? CastClipBySlotLabel { get; set; }
}

internal sealed class ClipMapSelection
{
    public static readonly ClipMapSelection Empty = new();

    public string IdleClipId { get; private set; } = string.Empty;
    public string MoveClipId { get; private set; } = string.Empty;
    public string RunForwardClipId { get; private set; } = string.Empty;
    public string RunBackwardClipId { get; private set; } = string.Empty;
    public string RunLeftClipId { get; private set; } = string.Empty;
    public string RunRightClipId { get; private set; } = string.Empty;
    public string RunForwardLeftClipId { get; private set; } = string.Empty;
    public string RunForwardRightClipId { get; private set; } = string.Empty;
    public string RunBackwardLeftClipId { get; private set; } = string.Empty;
    public string RunBackwardRightClipId { get; private set; } = string.Empty;
    public string StrafeLeftClipId { get; private set; } = string.Empty;
    public string StrafeRightClipId { get; private set; } = string.Empty;
    public string StrafeForwardLeftClipId { get; private set; } = string.Empty;
    public string StrafeForwardRightClipId { get; private set; } = string.Empty;
    public string StrafeBackwardLeftClipId { get; private set; } = string.Empty;
    public string StrafeBackwardRightClipId { get; private set; } = string.Empty;
    public string TurnLeftClipId { get; private set; } = string.Empty;
    public string TurnRightClipId { get; private set; } = string.Empty;
    public string BlockLoopClipId { get; private set; } = string.Empty;
    public string HeavyClipId { get; private set; } = string.Empty;
    public List<string> FastChainClipIds { get; } = new();
    public Dictionary<string, string> CastClipBySlotLabel { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ClipMapSelection From(ClipMapDefinition? def)
    {
        var s = new ClipMapSelection();
        if (def is null)
        {
            return s;
        }

        s.Apply(def);
        return s;
    }

    public void Apply(ClipMapDefinition def)
    {
        if (!string.IsNullOrWhiteSpace(def.IdleClipId)) IdleClipId = def.IdleClipId!;
        if (!string.IsNullOrWhiteSpace(def.MoveClipId)) MoveClipId = def.MoveClipId!;
        if (!string.IsNullOrWhiteSpace(def.RunForwardClipId)) RunForwardClipId = def.RunForwardClipId!;
        if (!string.IsNullOrWhiteSpace(def.RunBackwardClipId)) RunBackwardClipId = def.RunBackwardClipId!;
        if (!string.IsNullOrWhiteSpace(def.RunLeftClipId)) RunLeftClipId = def.RunLeftClipId!;
        if (!string.IsNullOrWhiteSpace(def.RunRightClipId)) RunRightClipId = def.RunRightClipId!;
        if (!string.IsNullOrWhiteSpace(def.RunForwardLeftClipId)) RunForwardLeftClipId = def.RunForwardLeftClipId!;
        if (!string.IsNullOrWhiteSpace(def.RunForwardRightClipId)) RunForwardRightClipId = def.RunForwardRightClipId!;
        if (!string.IsNullOrWhiteSpace(def.RunBackwardLeftClipId)) RunBackwardLeftClipId = def.RunBackwardLeftClipId!;
        if (!string.IsNullOrWhiteSpace(def.RunBackwardRightClipId)) RunBackwardRightClipId = def.RunBackwardRightClipId!;
        if (!string.IsNullOrWhiteSpace(def.StrafeLeftClipId)) StrafeLeftClipId = def.StrafeLeftClipId!;
        if (!string.IsNullOrWhiteSpace(def.StrafeRightClipId)) StrafeRightClipId = def.StrafeRightClipId!;
        if (!string.IsNullOrWhiteSpace(def.StrafeForwardLeftClipId)) StrafeForwardLeftClipId = def.StrafeForwardLeftClipId!;
        if (!string.IsNullOrWhiteSpace(def.StrafeForwardRightClipId)) StrafeForwardRightClipId = def.StrafeForwardRightClipId!;
        if (!string.IsNullOrWhiteSpace(def.StrafeBackwardLeftClipId)) StrafeBackwardLeftClipId = def.StrafeBackwardLeftClipId!;
        if (!string.IsNullOrWhiteSpace(def.StrafeBackwardRightClipId)) StrafeBackwardRightClipId = def.StrafeBackwardRightClipId!;
        if (!string.IsNullOrWhiteSpace(def.TurnLeftClipId)) TurnLeftClipId = def.TurnLeftClipId!;
        if (!string.IsNullOrWhiteSpace(def.TurnRightClipId)) TurnRightClipId = def.TurnRightClipId!;
        if (!string.IsNullOrWhiteSpace(def.BlockLoopClipId)) BlockLoopClipId = def.BlockLoopClipId!;
        if (!string.IsNullOrWhiteSpace(def.HeavyClipId)) HeavyClipId = def.HeavyClipId!;

        if (def.FastChainClipIds is not null && def.FastChainClipIds.Count > 0)
        {
            FastChainClipIds.Clear();
            for (var i = 0; i < def.FastChainClipIds.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(def.FastChainClipIds[i]))
                {
                    FastChainClipIds.Add(def.FastChainClipIds[i]);
                }
            }
        }

        if (def.CastClipBySlotLabel is not null)
        {
            foreach (var pair in def.CastClipBySlotLabel)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    CastClipBySlotLabel[pair.Key] = pair.Value;
                }
            }
        }
    }
}
