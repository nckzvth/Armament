#nullable enable
using System.Collections.Generic;
using Armament.Client.Animation;
using Armament.SharedSim.Protocol;
using UnityEngine;

namespace Armament.Client.Networking
{

public sealed class WorldDebugRenderer : MonoBehaviour
{
    [SerializeField] private float tileScale = 0.8f;

    private readonly Dictionary<uint, GameObject> entityViews = new();
    private readonly Dictionary<uint, LocalViewAtlasAnimator> localViewAnimators = new();
    private readonly List<uint> staleViewIds = new();
    private readonly Dictionary<InputActionFlags, GameObject> localActionViews = new();
    private readonly Dictionary<InputActionFlags, float> localActionVisibleUntil = new();
    private readonly List<CastPulse> authoritativeCastPulses = new();
    private uint lastAuthoritativeCastEventId;
    private GameObject? blockAuraView;
    private UdpGameClient? client;

    private void Awake()
    {
        client = GetComponent<UdpGameClient>() ?? FindFirstObjectByType<UdpGameClient>();
    }

    private void Update()
    {
        if (client is null)
        {
            return;
        }

        staleViewIds.Clear();
        foreach (var entityId in entityViews.Keys)
        {
            if (!client.RenderEntities.ContainsKey(entityId))
            {
                staleViewIds.Add(entityId);
            }
        }

        for (var i = 0; i < staleViewIds.Count; i++)
        {
            var id = staleViewIds[i];
            if (entityViews.TryGetValue(id, out var stale))
            {
                Destroy(stale);
            }

            entityViews.Remove(id);
            localViewAnimators.Remove(id);
        }

        foreach (var kvp in client.RenderEntities)
        {
            if (!entityViews.TryGetValue(kvp.Key, out var view))
            {
                view = CreateView(kvp.Key);
                entityViews[kvp.Key] = view;
            }

            view.transform.position = new Vector3(kvp.Value.x, kvp.Value.y, 0f);
            view.transform.localScale = ResolveScale(kvp.Key);
            if (localViewAnimators.TryGetValue(kvp.Key, out var animator))
            {
                animator.Tick(kvp.Value);
            }
        }

        UpdateLocalActionIndicators();
        UpdateAuthoritativeCastIndicators();
        TickAuthoritativeCastIndicators();
    }

    private void OnGUI()
    {
        if (client is null)
        {
            return;
        }

        var camera = Camera.main;
        if (camera is null)
        {
            return;
        }

        foreach (var kvp in client.RenderEntities)
        {
            if (!client.LatestEntityKinds.TryGetValue(kvp.Key, out var kind) || kind != Armament.SharedSim.Protocol.EntityKind.Loot)
            {
                continue;
            }

            var amount = client.LootCurrencyById.TryGetValue(kvp.Key, out var lootAmount) ? lootAmount : (ushort)0;
            var isPortal = kvp.Key == 900_001;
            if (isPortal && !ShouldShowPortalPrompt(kvp.Value))
            {
                continue;
            }

            if (!isPortal && !client.ShowLootNames)
            {
                continue;
            }

            var screenPos = camera.WorldToScreenPoint(new Vector3(kvp.Value.x, kvp.Value.y + 0.9f, 0f));
            if (screenPos.z < 0f)
            {
                continue;
            }

            var label = ResolveLootLabel(kvp.Key, amount, isPortal);
            var width = 90f;
            var rect = new Rect(screenPos.x - width * 0.5f, Screen.height - screenPos.y - 22f, width, 20f);
            GUI.Label(rect, label);
        }

        foreach (var kvp in client.RenderEntities)
        {
            if (!client.LatestEntityKinds.TryGetValue(kvp.Key, out var kind) || kind != Armament.SharedSim.Protocol.EntityKind.Enemy)
            {
                continue;
            }

            if (!client.LatestEntityHealth.TryGetValue(kvp.Key, out var hp))
            {
                continue;
            }

            var screenPos = camera.WorldToScreenPoint(new Vector3(kvp.Value.x, kvp.Value.y + 0.7f, 0f));
            if (screenPos.z < 0f)
            {
                continue;
            }

            var rect = new Rect(screenPos.x - 70f, Screen.height - screenPos.y - 18f, 140f, 20f);
            var stacks = client.LatestEnemyPrimaryStatusStacks.TryGetValue(kvp.Key, out var statusStacks) ? statusStacks : (byte)0;
            var statusSuffix = stacks > 0 ? $" | Stacks {stacks}" : string.Empty;
            GUI.Label(rect, $"HP {hp}{statusSuffix}");
        }

        foreach (var kvp in client.RenderEntities)
        {
            if (!client.LatestEntityKinds.TryGetValue(kvp.Key, out var kind) || kind != Armament.SharedSim.Protocol.EntityKind.Zone)
            {
                continue;
            }

            var screenPos = camera.WorldToScreenPoint(new Vector3(kvp.Value.x, kvp.Value.y + 0.15f, 0f));
            if (screenPos.z < 0f)
            {
                continue;
            }

            var rect = new Rect(screenPos.x - 34f, Screen.height - screenPos.y - 14f, 68f, 20f);
            GUI.Label(rect, ResolveZoneLabel(kvp.Key));
        }

        foreach (var kvp in client.RenderEntities)
        {
            if (!client.LatestEntityKinds.TryGetValue(kvp.Key, out var kind) || kind != Armament.SharedSim.Protocol.EntityKind.Link)
            {
                continue;
            }

            if (!client.LatestLinkOwnerByEntity.TryGetValue(kvp.Key, out var ownerId) ||
                !client.LatestLinkTargetByEntity.TryGetValue(kvp.Key, out var targetId))
            {
                continue;
            }

            var screenPos = camera.WorldToScreenPoint(new Vector3(kvp.Value.x, kvp.Value.y + 0.4f, 0f));
            if (screenPos.z < 0f)
            {
                continue;
            }

            var rect = new Rect(screenPos.x - 60f, Screen.height - screenPos.y - 14f, 120f, 20f);
            GUI.Label(rect, $"Tether {ownerId}->{targetId}");
        }

        if (client.LocalEntityId != 0 &&
            client.RenderEntities.TryGetValue(client.LocalEntityId, out var localPosition) &&
            localViewAnimators.TryGetValue(client.LocalEntityId, out var localAnimator))
        {
            var screenPos = camera.WorldToScreenPoint(new Vector3(localPosition.x, localPosition.y + 1.35f, 0f));
            if (screenPos.z >= 0f)
            {
                var label = $"Anim: {localAnimator.ActiveClipLabel}";
                var rect = new Rect(screenPos.x - 120f, Screen.height - screenPos.y - 18f, 240f, 20f);
                GUI.Label(rect, label);
            }
        }
    }

    private Color ResolveColor(uint entityId)
    {
        if (entityId == client!.LocalEntityId)
        {
            return Color.green;
        }

        if (client!.LatestEntityKinds.TryGetValue(entityId, out var kind))
        {
            if (kind == Armament.SharedSim.Protocol.EntityKind.Enemy) return Color.red;
            if (kind == Armament.SharedSim.Protocol.EntityKind.Zone)
            {
                if (client.LatestEntitySpenderResource.TryGetValue(entityId, out var zoneCode))
                {
                    return zoneCode switch
                    {
                        1 => new Color(0.4f, 0.95f, 1f, 0.55f),
                        2 => new Color(1f, 0.55f, 0.18f, 0.50f),
                        3 => new Color(1f, 0.25f, 0.25f, 0.52f),
                        4 => new Color(0.2f, 0.9f, 0.5f, 0.52f),
                        5 => new Color(0.15f, 0.75f, 1f, 0.58f),
                        6 => new Color(0.2f, 0.65f, 1f, 0.55f),
                        7 => new Color(0.9f, 0.35f, 1f, 0.58f),
                        8 => new Color(0.95f, 0.95f, 0.45f, 0.56f),
                        9 => new Color(1f, 0.75f, 0.2f, 0.58f),
                        _ => new Color(0.75f, 0.85f, 1f, 0.48f)
                    };
                }

                return new Color(0.4f, 0.95f, 1f, 0.55f);
            }
            if (kind == Armament.SharedSim.Protocol.EntityKind.Link)
            {
                return new Color(1f, 0.2f, 1f, 0.9f);
            }
            if (kind == Armament.SharedSim.Protocol.EntityKind.Loot)
            {
                if (entityId == 900_001)
                {
                    return new Color(1f, 0.5f, 0f);
                }

                if (client.LootCurrencyById.TryGetValue(entityId, out var amount) && amount == 0)
                {
                    return new Color(0.95f, 0.2f, 1f);
                }

                return Color.yellow;
            }
        }

        return Color.cyan;
    }

    private Vector3 ResolveScale(uint entityId)
    {
        if (client!.LatestEntityKinds.TryGetValue(entityId, out var kind) && kind == Armament.SharedSim.Protocol.EntityKind.Zone)
        {
            var radiusUnits = client.LatestEntityBuilderResource.TryGetValue(entityId, out var encodedRadius) ? encodedRadius : (ushort)18;
            var scale = Mathf.Clamp(radiusUnits / 7.5f, 1.4f, 4.5f);
            return Vector3.one * (tileScale * scale);
        }

        if (client!.LatestEntityKinds.TryGetValue(entityId, out var linkKind) && linkKind == Armament.SharedSim.Protocol.EntityKind.Link)
        {
            return Vector3.one * (tileScale * 0.35f);
        }

        if (client!.LatestEntityKinds.TryGetValue(entityId, out var lootKind) && lootKind == Armament.SharedSim.Protocol.EntityKind.Loot)
        {
            if (entityId == 900_001)
            {
                return Vector3.one * (tileScale * 0.8f);
            }

            if (client.LootCurrencyById.TryGetValue(entityId, out var amount) && amount == 0)
            {
                return Vector3.one * (tileScale * 0.7f);
            }

            return Vector3.one * (tileScale * 0.6f);
        }

        return Vector3.one * tileScale;
    }

    private GameObject CreateView(uint entityId)
    {
        var go = new GameObject("PlayerView");
        var renderer = go.AddComponent<SpriteRenderer>();
        var color = ResolveColor(entityId);
        if (client!.LatestEntityKinds.TryGetValue(entityId, out var kind) && kind == Armament.SharedSim.Protocol.EntityKind.Zone)
        {
            renderer.sprite = BuildRingSprite(color);
            renderer.sortingOrder = 1;
        }
        else
        {
            renderer.sprite = BuildSquareSprite(color);
        }

        if (entityId == client!.LocalEntityId)
        {
            var animator = go.AddComponent<LocalViewAtlasAnimator>();
            if (animator.TryInitialize(client, renderer))
            {
                localViewAnimators[entityId] = animator;
            }
        }

        go.transform.localScale = Vector3.one * tileScale;
        return go;
    }

    private static Sprite BuildSquareSprite(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    private string ResolveLootLabel(uint entityId, ushort amount, bool isPortal)
    {
        if (isPortal)
        {
            return "[F] Dungeon";
        }

        if (amount == 0)
        {
            return "Test Relic";
        }

        return $"Gold x{amount}";
    }

    private bool ShouldShowPortalPrompt(Vector2 portalPos)
    {
        if (client is null || client.CurrentZone != Armament.SharedSim.Protocol.ZoneKind.Overworld)
        {
            return false;
        }

        if (!client.RenderEntities.TryGetValue(client.LocalEntityId, out var playerPos))
        {
            return false;
        }

        return (playerPos - portalPos).sqrMagnitude <= 6.25f;
    }

    private void UpdateLocalActionIndicators()
    {
        if (client is null || !client.RenderEntities.TryGetValue(client.LocalEntityId, out var localPos))
        {
            HideLocalIndicators();
            return;
        }

        var flags = client.LocalActionFlags;
        var now = Time.unscaledTime;
        var actionDefs = GetActionDefinitions();
        for (var i = 0; i < actionDefs.Length; i++)
        {
            var def = actionDefs[i];
            EnsureActionView(def.Flag, def.Color);

            var isActiveNow = flags.HasFlag(def.Flag);
            if (isActiveNow)
            {
                localActionVisibleUntil[def.Flag] = now + def.HoldSeconds;
            }

            var shouldShow = localActionVisibleUntil.TryGetValue(def.Flag, out var until) && until >= now;
            if (!localActionViews.TryGetValue(def.Flag, out var view))
            {
                continue;
            }

            view.SetActive(shouldShow);
            if (!shouldShow)
            {
                continue;
            }

            view.transform.position = new Vector3(localPos.x + def.Offset.x, localPos.y + def.Offset.y, 0f);
            view.transform.localScale = Vector3.one * def.Scale;
        }

        EnsureBlockAura();
        var blocking = flags.HasFlag(InputActionFlags.BlockHold);
        if (blockAuraView is not null)
        {
            blockAuraView.SetActive(blocking);
            if (blocking)
            {
                blockAuraView.transform.position = new Vector3(localPos.x, localPos.y, 0f);
            }
        }
    }

    private void EnsureActionView(InputActionFlags flag, Color color)
    {
        if (localActionViews.ContainsKey(flag))
        {
            return;
        }

        var go = new GameObject($"Action_{flag}");
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = BuildSquareSprite(color);
        renderer.sortingOrder = 5;
        localActionViews[flag] = go;
        localActionVisibleUntil[flag] = 0f;
        go.SetActive(false);
    }

    private void EnsureBlockAura()
    {
        if (blockAuraView is not null)
        {
            return;
        }

        blockAuraView = new GameObject("BlockAura");
        var renderer = blockAuraView.AddComponent<SpriteRenderer>();
        renderer.sprite = BuildRingSprite(new Color(0.45f, 0.95f, 1f, 0.85f));
        renderer.sortingOrder = 4;
        blockAuraView.transform.localScale = Vector3.one * (tileScale * 1.8f);
        blockAuraView.SetActive(false);
    }

    private void HideLocalIndicators()
    {
        foreach (var view in localActionViews.Values)
        {
            view.SetActive(false);
        }

        if (blockAuraView is not null)
        {
            blockAuraView.SetActive(false);
        }
    }

    private void UpdateAuthoritativeCastIndicators()
    {
        if (client is null || client.LastAuthoritativeCastEventId == 0)
        {
            return;
        }

        if (client.LastAuthoritativeCastEventId == lastAuthoritativeCastEventId)
        {
            return;
        }

        lastAuthoritativeCastEventId = client.LastAuthoritativeCastEventId;
        if (client.LastAuthoritativeCastResultCode != 1)
        {
            return;
        }

        if (!client.RenderEntities.TryGetValue(client.LocalEntityId, out var localPos))
        {
            return;
        }

        var pulse = new GameObject($"AuthCastPulse_{client.LastAuthoritativeCastEventId}");
        var renderer = pulse.AddComponent<SpriteRenderer>();
        renderer.sprite = BuildRingSprite(ResolveCastPulseColor(client.LastAuthoritativeCastTargetTeamCode, client.LastAuthoritativeCastVfxCode));
        renderer.sortingOrder = 8;
        var scale = tileScale * Mathf.Clamp(0.9f + client.LastAuthoritativeCastAffectedCount * 0.12f, 0.9f, 2.1f);
        pulse.transform.localScale = Vector3.one * scale;
        pulse.transform.position = new Vector3(localPos.x, localPos.y, 0f);
        authoritativeCastPulses.Add(new CastPulse(pulse, Time.unscaledTime + 0.35f));
    }

    private void TickAuthoritativeCastIndicators()
    {
        if (authoritativeCastPulses.Count == 0 || client is null)
        {
            return;
        }

        var now = Time.unscaledTime;
        var localPosFound = client.RenderEntities.TryGetValue(client.LocalEntityId, out var localPos);
        for (var i = authoritativeCastPulses.Count - 1; i >= 0; i--)
        {
            var pulse = authoritativeCastPulses[i];
            if (pulse.View is null)
            {
                authoritativeCastPulses.RemoveAt(i);
                continue;
            }

            if (localPosFound)
            {
                pulse.View.transform.position = new Vector3(localPos.x, localPos.y, 0f);
            }

            if (now > pulse.ExpiresAtSeconds)
            {
                Destroy(pulse.View);
                authoritativeCastPulses.RemoveAt(i);
            }
        }
    }

    private static Sprite BuildRingSprite(Color color)
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var center = (size - 1) * 0.5f;
        var outer = center;
        var inner = outer * 0.65f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var pixel = (d <= outer && d >= inner) ? color : Color.clear;
                tex.SetPixel(x, y, pixel);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Color ResolveCastPulseColor(byte targetTeamCode, ushort vfxCode)
    {
        var hue = (vfxCode % 360) / 360f;
        var baseColor = Color.HSVToRGB(hue, 0.75f, 1f);
        var teamTint = targetTeamCode switch
        {
            1 => new Color(1f, 0.2f, 0.2f, 0.85f),
            2 => new Color(0.2f, 1f, 0.4f, 0.85f),
            3 => new Color(0.95f, 0.95f, 0.3f, 0.85f),
            4 => new Color(0.25f, 0.9f, 1f, 0.85f),
            _ => new Color(0.85f, 0.85f, 0.85f, 0.85f)
        };

        return Color.Lerp(baseColor, teamTint, 0.55f);
    }

    private static ActionVisualDef[] GetActionDefinitions()
    {
        return new[]
        {
            new ActionVisualDef(InputActionFlags.FastAttackHold, new Vector2(-0.9f, 1.0f), new Color(1f, 0.78f, 0.1f), 0.22f, 0.12f),
            new ActionVisualDef(InputActionFlags.HeavyAttackHold, new Vector2(0.9f, 1.0f), new Color(1f, 0.35f, 0.05f), 0.24f, 0.12f),
            new ActionVisualDef(InputActionFlags.Skill1, new Vector2(-1.15f, 0.55f), new Color(0.2f, 0.9f, 1f), 0.20f, 0.22f),
            new ActionVisualDef(InputActionFlags.Skill2, new Vector2(-0.55f, 0.55f), new Color(0.18f, 1f, 0.55f), 0.20f, 0.22f),
            new ActionVisualDef(InputActionFlags.Skill3, new Vector2(0.05f, 0.55f), new Color(0.55f, 0.95f, 0.2f), 0.20f, 0.22f),
            new ActionVisualDef(InputActionFlags.Skill4, new Vector2(0.65f, 0.55f), new Color(1f, 0.9f, 0.2f), 0.20f, 0.22f),
            new ActionVisualDef(InputActionFlags.Skill5, new Vector2(-1.15f, -0.05f), new Color(1f, 0.55f, 0.2f), 0.20f, 0.22f),
            new ActionVisualDef(InputActionFlags.Skill6, new Vector2(-0.55f, -0.05f), new Color(1f, 0.35f, 0.65f), 0.20f, 0.22f),
            new ActionVisualDef(InputActionFlags.Skill7, new Vector2(0.05f, -0.05f), new Color(0.75f, 0.4f, 1f), 0.20f, 0.22f),
            new ActionVisualDef(InputActionFlags.Skill8, new Vector2(0.65f, -0.05f), new Color(0.45f, 0.65f, 1f), 0.20f, 0.22f)
        };
    }

    private string ResolveZoneLabel(uint entityId)
    {
        if (!client!.LatestEntitySpenderResource.TryGetValue(entityId, out var zoneCode))
        {
            return "Zone";
        }

        return zoneCode switch
        {
            1 => "Ward",
            2 => "Fissure",
            3 => "Caldera",
            4 => "Pool",
            5 => "Maelstrom",
            6 => "Vortex",
            7 => "Storm",
            8 => "Constellation",
            9 => "Decree",
            _ => "Zone"
        };
    }

    private readonly struct ActionVisualDef
    {
        public ActionVisualDef(InputActionFlags flag, Vector2 offset, Color color, float scale, float holdSeconds)
        {
            Flag = flag;
            Offset = offset;
            Color = color;
            Scale = scale;
            HoldSeconds = holdSeconds;
        }

        public InputActionFlags Flag { get; }
        public Vector2 Offset { get; }
        public Color Color { get; }
        public float Scale { get; }
        public float HoldSeconds { get; }
    }

    private readonly struct CastPulse
    {
        public CastPulse(GameObject view, float expiresAtSeconds)
        {
            View = view;
            ExpiresAtSeconds = expiresAtSeconds;
        }

        public GameObject View { get; }
        public float ExpiresAtSeconds { get; }
    }
}
}
