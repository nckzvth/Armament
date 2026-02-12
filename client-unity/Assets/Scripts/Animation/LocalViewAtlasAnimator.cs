#nullable enable
using System;
using System.Collections.Generic;
using Armament.Client.Networking;
using UnityEngine;

namespace Armament.Client.Animation
{

public sealed class LocalViewAtlasAnimator : MonoBehaviour
{
    private static readonly HashSet<string> WarnedInvalidClipIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeClip> clipsById = new(StringComparer.Ordinal);
    private UdpGameClient? client;
    private SpriteRenderer? spriteRenderer;
    private AtlasAnimationLibrary? library;
    private ClassAnimationMap? animationMap;

    private bool initialized;
    private bool warnedMissingLibrary;
    private uint lastCastEventId;
    private string currentClipId = string.Empty;
    private string oneShotClipId = string.Empty;
    private float currentClipTime;
    private int facingDirection;
    private int previousFacingDirection = -1;
    private Vector2 lastPosition;
    private bool hasLastPosition;
    private bool wasFastAttackHeld;
    private int fastAttackChainIndex;
    private float fastAttackChainTime;

    public string ActiveClipLabel => string.IsNullOrWhiteSpace(oneShotClipId) ? currentClipId : oneShotClipId;

    public bool TryInitialize(UdpGameClient sourceClient, SpriteRenderer targetRenderer)
    {
        client = sourceClient;
        spriteRenderer = targetRenderer;
        ReloadClassAssets();
        return initialized;
    }

    public void Tick(Vector2 worldPosition)
    {
        if (!initialized || client is null || spriteRenderer is null)
        {
            return;
        }

        var expectedLibraryPath = ResolveLibraryPath(client.BaseClassId);
        if (library is null || !string.Equals(expectedLibraryPath, library.name, StringComparison.OrdinalIgnoreCase))
        {
            ReloadClassAssets();
            if (!initialized)
            {
                return;
            }
        }

        var velocity = Vector2.zero;
        if (hasLastPosition)
        {
            velocity = (worldPosition - lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        }

        lastPosition = worldPosition;
        hasLastPosition = true;

        HandleAuthoritativeCastEvent();

        facingDirection = ResolveFacingDirection(worldPosition, velocity);

        var isMoving = client.LocalMoveInputVector.sqrMagnitude > 0.0001f || velocity.sqrMagnitude > 0.01f;
        var desiredLoopClip = ResolveLoopClip(isMoving);
        if (!string.IsNullOrEmpty(oneShotClipId))
        {
            if (TryAdvanceOneShot())
            {
                ApplyFrame(oneShotClipId, facingDirection);
                return;
            }

            oneShotClipId = string.Empty;
            currentClipTime = 0f;
        }

        if (!string.Equals(currentClipId, desiredLoopClip, StringComparison.Ordinal))
        {
            currentClipId = desiredLoopClip;
            currentClipTime = 0f;
        }

        AdvanceTime();
        ApplyFrame(currentClipId, facingDirection);
    }

    private void ReloadClassAssets()
    {
        if (client is null || spriteRenderer is null)
        {
            initialized = false;
            return;
        }

        var baseClass = NormalizeClassId(client.BaseClassId);
        library = Resources.Load<AtlasAnimationLibrary>($"Animation/{baseClass}.animationlibrary");
        animationMap = Resources.Load<ClassAnimationMap>($"Animation/{baseClass}.animationmap");
        if (library is null)
        {
            if (!warnedMissingLibrary)
            {
                warnedMissingLibrary = true;
                Debug.LogWarning($"[LocalViewAtlasAnimator] Missing class animation library Resources/Animation/{baseClass}.animationlibrary");
            }

            initialized = false;
            clipsById.Clear();
            return;
        }

        BuildRuntimeCache(library);
        if (clipsById.Count == 0)
        {
            initialized = false;
            return;
        }

        if (animationMap is null)
        {
            animationMap = BuildFallbackMap(clipsById);
        }

        initialized = true;
        currentClipId = ResolveAvailableClip(animationMap.idleClipId);
        currentClipTime = 0f;
        oneShotClipId = string.Empty;
        spriteRenderer.color = Color.white;
        spriteRenderer.sortingOrder = 10;
    }

    private void HandleAuthoritativeCastEvent()
    {
        if (client is null || animationMap is null || client.LastAuthoritativeCastEventId == 0 || client.LastAuthoritativeCastEventId == lastCastEventId)
        {
            return;
        }

        lastCastEventId = client.LastAuthoritativeCastEventId;
        if (client.LastAuthoritativeCastResultCode != 1)
        {
            return;
        }

        var castClip = MapCastSlotToClip(client.LastAuthoritativeCastSlotCode, animationMap);
        if (string.IsNullOrWhiteSpace(castClip))
        {
            return;
        }

        oneShotClipId = ResolveAvailableClip(castClip);
        currentClipTime = 0f;
    }

    private string ResolveLoopClip(bool moving)
    {
        if (client is null || animationMap is null)
        {
            return ResolveAvailableClip(string.Empty);
        }

        if (client.LocalActionFlags.HasFlag(Armament.SharedSim.Protocol.InputActionFlags.BlockHold))
        {
            return ResolveAvailableClip(animationMap.blockLoopClipId);
        }

        if (client.LocalActionFlags.HasFlag(Armament.SharedSim.Protocol.InputActionFlags.HeavyAttackHold))
        {
            return ResolveAvailableClip(animationMap.heavyAttackClipId);
        }

        if (client.LocalActionFlags.HasFlag(Armament.SharedSim.Protocol.InputActionFlags.FastAttackHold))
        {
            return ResolveFastAttackChainClip();
        }

        wasFastAttackHeld = false;
        fastAttackChainIndex = 0;
        fastAttackChainTime = 0f;

        return moving ? ResolveAvailableClip(animationMap.moveClipId) : ResolveAvailableClip(animationMap.idleClipId);
    }

    private string ResolveFastAttackChainClip()
    {
        if (animationMap is null)
        {
            return ResolveAvailableClip(string.Empty);
        }

        if (!wasFastAttackHeld)
        {
            wasFastAttackHeld = true;
            fastAttackChainIndex = 0;
            fastAttackChainTime = 0f;
        }

        var selected = ResolveAvailableClip(animationMap.GetFastAttackChainClip(fastAttackChainIndex));
        if (clipsById.TryGetValue(selected, out var runtimeClip))
        {
            fastAttackChainTime += Time.deltaTime;
            var duration = Mathf.Max(0.04f, runtimeClip.DurationSeconds);
            if (fastAttackChainTime >= duration)
            {
                fastAttackChainTime -= duration;
                fastAttackChainIndex++;
                selected = ResolveAvailableClip(animationMap.GetFastAttackChainClip(fastAttackChainIndex));
            }
        }

        return selected;
    }

    private static string MapCastSlotToClip(byte slotCode, ClassAnimationMap map)
    {
        return slotCode switch
        {
            0 => map.fastAttackClipId,
            1 => map.heavyAttackClipId,
            2 => map.blockLoopClipId,
            _ => map.GetCastClipForSlotCode(slotCode)
        };
    }

    private string ResolveAvailableClip(string preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && clipsById.ContainsKey(preferred))
        {
            return preferred;
        }

        foreach (var kvp in clipsById)
        {
            return kvp.Key;
        }

        return string.Empty;
    }

    private bool TryAdvanceOneShot()
    {
        if (!clipsById.TryGetValue(oneShotClipId, out var clip))
        {
            return false;
        }

        currentClipTime += Time.deltaTime;
        return currentClipTime <= clip.DurationSeconds;
    }

    private void AdvanceTime()
    {
        if (!clipsById.TryGetValue(currentClipId, out var clip) || clip.DurationSeconds <= 0f)
        {
            return;
        }

        currentClipTime += Time.deltaTime;
        while (currentClipTime > clip.DurationSeconds)
        {
            currentClipTime -= clip.DurationSeconds;
        }
    }

    private void ApplyFrame(string clipId, int direction)
    {
        if (spriteRenderer is null || string.IsNullOrWhiteSpace(clipId) || !clipsById.TryGetValue(clipId, out var clip))
        {
            return;
        }

        if (!clip.TryGetDirection(direction, out var frames) && !clip.TryGetDirection(0, out frames))
        {
            return;
        }

        if (frames.Length == 0)
        {
            return;
        }

        var frameIndex = 0;
        for (var i = 0; i < frames.Length; i++)
        {
            if (currentClipTime >= frames[i].TimeSeconds)
            {
                frameIndex = i;
            }
            else
            {
                break;
            }
        }

        spriteRenderer.sprite = frames[frameIndex].Sprite;
    }

    private void BuildRuntimeCache(AtlasAnimationLibrary source)
    {
        clipsById.Clear();

        for (var i = 0; i < source.Clips.Count; i++)
        {
            var clip = source.Clips[i];
            if (clip is null || clip.atlasTexture is null || string.IsNullOrWhiteSpace(clip.clipId) || clip.frames.Count == 0)
            {
                continue;
            }

            var runtime = RuntimeClip.Create(clip);
            if (runtime.TotalFrames == 0)
            {
                continue;
            }

            clipsById[clip.clipId] = runtime;
        }
    }

    private static ClassAnimationMap BuildFallbackMap(IReadOnlyDictionary<string, RuntimeClip> clips)
    {
        var map = ScriptableObject.CreateInstance<ClassAnimationMap>();
        map.idleClipId = FindClipContaining(clips, "idle") ?? FindAny(clips);
        map.moveClipId = FindClipContaining(clips, "run01_forward") ?? FindClipContaining(clips, "run") ?? map.idleClipId;
        map.blockLoopClipId = FindClipContaining(clips, "blockshield01 - loop") ??
                              FindClipContaining(clips, "blockshield01") ??
                              FindClipContaining(clips, "block") ??
                              map.idleClipId;
        map.fastAttackClipId = FindClipContaining(clips, "attackshield01") ?? FindClipContaining(clips, "attack-r1") ?? map.idleClipId;
        map.heavyAttackClipId = FindClipContaining(clips, "attackshield02") ?? FindClipContaining(clips, "attack-r2") ?? map.fastAttackClipId;
        map.stunClipId = FindClipContaining(clips, "stun") ?? map.heavyAttackClipId;
        map.hitReactClipId = FindClipContaining(clips, "blockshield01 - hit") ?? map.stunClipId;
        map.deathClipId = FindClipContaining(clips, "death") ?? map.stunClipId;
        map.lootClipId = map.idleClipId;
        map.interactClipId = map.idleClipId;
        map.fastAttackChainClipIds = new[]
        {
            FindClipContaining(clips, "attackshield01") ?? map.fastAttackClipId,
            FindClipContaining(clips, "unarmed-attack-r1") ?? FindClipContaining(clips, "attack-r1") ?? map.fastAttackClipId,
            FindClipContaining(clips, "attackshield02") ?? map.heavyAttackClipId
        };
        map.castClipIds = new[]
        {
            FindClipContaining(clips, "attack-r1") ?? map.fastAttackClipId,
            FindClipContaining(clips, "attack-r2") ?? map.heavyAttackClipId,
            FindClipContaining(clips, "attack-r3") ?? map.heavyAttackClipId,
            map.heavyAttackClipId,
            map.fastAttackClipId,
            map.stunClipId,
            map.heavyAttackClipId,
            map.heavyAttackClipId
        };
        return map;
    }

    private static string? FindClipContaining(IReadOnlyDictionary<string, RuntimeClip> clips, string token)
    {
        foreach (var kvp in clips)
        {
            if (kvp.Key.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return kvp.Key;
            }
        }

        return null;
    }

    private static string FindAny(IReadOnlyDictionary<string, RuntimeClip> clips)
    {
        foreach (var kvp in clips)
        {
            return kvp.Key;
        }

        return string.Empty;
    }

    private int ResolveFacingDirection(Vector2 worldPosition, Vector2 velocity)
    {
        Vector2 facingVector;
        if (client is not null && client.HasAimWorldPosition)
        {
            facingVector = client.AimWorldPosition - worldPosition;
        }
        else
        {
            facingVector = velocity;
        }

        var result = QuantizeDirectionStable(facingVector, previousFacingDirection);
        previousFacingDirection = result;
        return result;
    }

    private static int QuantizeDirectionStable(Vector2 vector, int previousDirection)
    {
        if (vector.sqrMagnitude < 0.0001f)
        {
            return previousDirection < 0 ? 0 : previousDirection;
        }

        var angle = Mathf.Atan2(vector.x, vector.y) * Mathf.Rad2Deg;
        if (angle < 0f)
        {
            angle += 360f;
        }

        var idx = Mathf.RoundToInt(angle / 45f) % 8;
        if (previousDirection >= 0)
        {
            var previousCenter = previousDirection * 45f;
            var delta = Mathf.Abs(Mathf.DeltaAngle(previousCenter, angle));
            if (delta < 8f)
            {
                idx = previousDirection;
            }
        }

        return idx < 0 ? idx + 8 : idx;
    }

    private static string NormalizeClassId(string? input)
    {
        return string.IsNullOrWhiteSpace(input) ? "bastion" : input.Trim().ToLowerInvariant();
    }

    private static string ResolveLibraryPath(string? classId)
    {
        return NormalizeClassId(classId) + ".animationlibrary";
    }

    private static bool TryBuildSpriteFrame(AtlasAnimationClip clip, AtlasAnimationFrame frame, out Sprite? sprite)
    {
        sprite = null;
        var texture = clip.atlasTexture;
        if (texture is null)
        {
            return false;
        }

        var textureWidth = texture.width;
        var textureHeight = texture.height;
        if (textureWidth <= 0 || textureHeight <= 0 || frame.w <= 0 || frame.h <= 0)
        {
            return false;
        }

        var atlasW = Mathf.Max(1, clip.atlasWidth);
        var atlasH = Mathf.Max(1, clip.atlasHeight);
        var scaleX = (float)textureWidth / atlasW;
        var scaleY = (float)textureHeight / atlasH;

        var rawX = Mathf.RoundToInt(frame.x * scaleX);
        var sourceY = clip.row0AtTop
            ? Mathf.Max(0, clip.atlasHeight - frame.y - frame.h)
            : Mathf.Max(0, frame.y);
        var rawY = Mathf.RoundToInt(sourceY * scaleY);

        if (rawX >= textureWidth || rawY >= textureHeight)
        {
            return false;
        }

        var frameW = Mathf.Max(1, Mathf.RoundToInt(frame.w * scaleX));
        var frameH = Mathf.Max(1, Mathf.RoundToInt(frame.h * scaleY));
        var clampedW = Mathf.Min(frameW, textureWidth - rawX);
        var clampedH = Mathf.Min(frameH, textureHeight - rawY);
        if (clampedW <= 0 || clampedH <= 0)
        {
            return false;
        }

        var rect = new Rect(rawX, rawY, clampedW, clampedH);
        // Use a stable cell-space pivot basis to avoid visual "sliding" caused by per-frame crop-size variance.
        var pivotBasis = Mathf.Max(1f, clip.pixelsPerUnit);
        var pivot = new Vector2(
            Mathf.Clamp01((float)frame.pivotX / pivotBasis),
            Mathf.Clamp01((float)frame.pivotY / pivotBasis));

        try
        {
            sprite = Sprite.Create(texture, rect, pivot, Mathf.Max(1, clip.pixelsPerUnit));
            return sprite is not null;
        }
        catch
        {
            return false;
        }
    }

    private sealed class RuntimeClip
    {
        private readonly Dictionary<int, RuntimeFrame[]> framesByDirection;

        private RuntimeClip(Dictionary<int, RuntimeFrame[]> framesByDirection, float durationSeconds, int totalFrames)
        {
            this.framesByDirection = framesByDirection;
            DurationSeconds = durationSeconds;
            TotalFrames = totalFrames;
        }

        public float DurationSeconds { get; }
        public int TotalFrames { get; }

        public bool TryGetDirection(int direction, out RuntimeFrame[] frames)
        {
            return framesByDirection.TryGetValue(direction, out frames!);
        }

        public static RuntimeClip Create(AtlasAnimationClip clip)
        {
            var grouped = new Dictionary<int, List<RuntimeFrame>>();
            var invalidFrames = 0;
            for (var i = 0; i < clip.frames.Count; i++)
            {
                var frame = clip.frames[i];
                if (!TryBuildSpriteFrame(clip, frame, out var sprite) || sprite is null)
                {
                    invalidFrames++;
                    continue;
                }
                var runtimeFrame = new RuntimeFrame(frame.time, sprite);

                if (!grouped.TryGetValue(frame.direction, out var list))
                {
                    list = new List<RuntimeFrame>();
                    grouped[frame.direction] = list;
                }

                list.Add(runtimeFrame);
            }

            if (invalidFrames > 0 && WarnedInvalidClipIds.Add(clip.clipId))
            {
                Debug.LogWarning($"[LocalViewAtlasAnimator] Clip '{clip.clipId}' skipped {invalidFrames} invalid frame(s) due to atlas bounds mismatch.");
            }

            var final = new Dictionary<int, RuntimeFrame[]>();
            var maxDuration = 0f;
            var total = 0;
            foreach (var kvp in grouped)
            {
                kvp.Value.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
                var arr = kvp.Value.ToArray();
                final[kvp.Key] = arr;
                total += arr.Length;
                if (arr.Length > 0)
                {
                    var duration = arr[arr.Length - 1].TimeSeconds + Mathf.Max(0.01f, 1f / Mathf.Max(1f, clip.fps));
                    if (duration > maxDuration)
                    {
                        maxDuration = duration;
                    }
                }
            }

            if (maxDuration <= 0f)
            {
                maxDuration = 0.5f;
            }

            return new RuntimeClip(final, maxDuration, total);
        }
    }

    private readonly struct RuntimeFrame
    {
        public RuntimeFrame(float timeSeconds, Sprite sprite)
        {
            TimeSeconds = timeSeconds;
            Sprite = sprite;
        }

        public float TimeSeconds { get; }
        public Sprite Sprite { get; }
    }
}
}
