#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Armament.Client.Networking;

public sealed class WorldDebugRenderer : MonoBehaviour
{
    [SerializeField] private float tileScale = 0.8f;

    private readonly Dictionary<uint, GameObject> entityViews = new();
    private readonly List<uint> staleViewIds = new();
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
        }

        foreach (var kvp in client.RenderEntities)
        {
            if (!entityViews.TryGetValue(kvp.Key, out var view))
            {
                view = CreateView(ResolveColor(kvp.Key));
                entityViews[kvp.Key] = view;
            }

            view.transform.position = new Vector3(kvp.Value.x, kvp.Value.y, 0f);
            view.transform.localScale = ResolveScale(kvp.Key);
        }
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
        if (client!.LatestEntityKinds.TryGetValue(entityId, out var kind) && kind == Armament.SharedSim.Protocol.EntityKind.Loot)
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

    private GameObject CreateView(Color color)
    {
        var go = new GameObject("PlayerView");
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = BuildSquareSprite(color);
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
}
