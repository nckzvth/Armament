#nullable enable
using Armament.SharedSim.Protocol;
using UnityEngine;

namespace Armament.Client.Networking
{

public sealed class DebugHud : MonoBehaviour
{
    private UdpGameClient? client;
    private Vector2 serverFeedScroll;
    private bool serverFeedExpanded;

    private void Awake()
    {
        client = GetComponent<UdpGameClient>() ?? FindFirstObjectByType<UdpGameClient>();
    }

    private void OnGUI()
    {
        if (client is null || !client.IsJoined)
        {
            return;
        }

        const float margin = 10f;
        const float lineHeight = 18f;
        const float inset = 8f;
        var topStripHeight = 48f;
        var topRect = new Rect(margin, margin, Screen.width - margin * 2f, topStripHeight);
        var feedWidth = Mathf.Min(420f, Screen.width * 0.31f);
        var feedHeight = 108f;
        var feedRect = new Rect(
            margin,
            Screen.height - margin - feedHeight,
            feedWidth,
            feedHeight);
        var serverFeedHeight = serverFeedExpanded ? 220f : feedHeight;
        var authoritativeFeedRect = new Rect(
            feedRect.xMax + margin,
            Screen.height - margin - serverFeedHeight,
            feedWidth,
            serverFeedHeight);

        GUI.Box(topRect, string.Empty);
        GUI.Box(feedRect, string.Empty);
        GUI.Box(authoritativeFeedRect, string.Empty);

        var y = topRect.y + inset;
        var x = topRect.x + inset;
        var width = topRect.width - inset * 2f;
        var deadText = client.LocalHealth == 0 ? " | DEAD(H)" : string.Empty;
        var linkCount = 0;
        foreach (var kind in client.LatestEntityKinds.Values)
        {
            if (kind == EntityKind.Link)
            {
                linkCount++;
            }
        }
        GUI.Label(
            new Rect(x, y, width, lineHeight),
            $"Armament | Ent {client.LocalEntityId} | {client.BaseClassId}/{client.SpecId} | {client.CurrentZone}:{client.CurrentInstanceId} | HP {client.LocalHealth}{deadText} | B {client.LocalBuilderResource} | S {client.LocalSpenderResource} | $ {client.LocalCurrency} | Links {linkCount} | Names {(client.ShowLootNames ? "ON" : "OFF")}");
        y += lineHeight;
        GUI.Label(
            new Rect(x, y, width, lineHeight),
            $"CD L/R {FormatCooldownShort(client.LocalCooldownTicks[0])}/{FormatCooldownShort(client.LocalCooldownTicks[1])} | E/R/Q/T {FormatCooldownShort(client.LocalCooldownTicks[2])}/{FormatCooldownShort(client.LocalCooldownTicks[3])}/{FormatCooldownShort(client.LocalCooldownTicks[4])}/{FormatCooldownShort(client.LocalCooldownTicks[5])} | 1/2/3/4 {FormatCooldownShort(client.LocalCooldownTicks[6])}/{FormatCooldownShort(client.LocalCooldownTicks[7])}/{FormatCooldownShort(client.LocalCooldownTicks[8])}/{FormatCooldownShort(client.LocalCooldownTicks[9])} | {BuildAggroLine(client)} | Burst x{client.LocalDebugConsumedStatusStacks}");

        y = feedRect.y + inset;
        x = feedRect.x + inset;
        width = feedRect.width - inset * 2f;
        GUI.Label(new Rect(x, y, width, lineHeight), "Combat");
        y += lineHeight;
        var events = client.RecentCombatEvents;
        var linesShown = 0;
        for (var i = events.Count - 1; i >= 0; i--)
        {
            GUI.Label(new Rect(x + 2f, y, width - 2f, lineHeight), $"- {events[i]}");
            y += lineHeight;
            linesShown++;
            if (linesShown >= 4 || y > feedRect.yMax - lineHeight)
            {
                break;
            }
        }

        y = authoritativeFeedRect.y + inset;
        x = authoritativeFeedRect.x + inset;
        width = authoritativeFeedRect.width - inset * 2f;
        GUI.Label(new Rect(x, y, width - 92f, lineHeight), "Server Cast Feed");
        if (GUI.Button(new Rect(x + width - 88f, y - 2f, 88f, lineHeight + 4f), serverFeedExpanded ? "Collapse" : "Expand"))
        {
            serverFeedExpanded = !serverFeedExpanded;
        }
        y += lineHeight;
        var authEvents = client.RecentAuthoritativeEvents;
        var viewport = new Rect(x, y, width, authoritativeFeedRect.height - (y - authoritativeFeedRect.y) - inset);
        var contentHeight = Mathf.Max(viewport.height, authEvents.Count * lineHeight + 4f);
        var contentRect = new Rect(0f, 0f, viewport.width - 18f, contentHeight);
        serverFeedScroll = GUI.BeginScrollView(viewport, serverFeedScroll, contentRect);
        var rowY = 2f;
        for (var i = authEvents.Count - 1; i >= 0; i--)
        {
            GUI.Label(new Rect(2f, rowY, contentRect.width - 4f, lineHeight), $"- {authEvents[i]}");
            rowY += lineHeight;
        }
        GUI.EndScrollView();
    }

    private static string FormatCooldownShort(byte ticks)
    {
        if (ticks == 0)
        {
            return "-";
        }

        var seconds = ticks / 60f;
        return $"{seconds:0.0}";
    }

    private static string BuildAggroLine(UdpGameClient client)
    {
        if (!client.RenderEntities.TryGetValue(client.LocalEntityId, out var playerPos))
        {
            return string.Empty;
        }

        var nearestEnemyId = 0u;
        var nearestDistSq = float.MaxValue;
        foreach (var kvp in client.RenderEntities)
        {
            if (!client.LatestEntityKinds.TryGetValue(kvp.Key, out var kind) || kind != Armament.SharedSim.Protocol.EntityKind.Enemy)
            {
                continue;
            }

            var distSq = (kvp.Value - playerPos).sqrMagnitude;
            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearestEnemyId = kvp.Key;
            }
        }

        if (nearestEnemyId == 0)
        {
            return "Aggro: no enemy";
        }

        var target = client.LatestEnemyAggroTarget.TryGetValue(nearestEnemyId, out var t) ? t : 0u;
        var threat = client.LatestEnemyAggroThreat.TryGetValue(nearestEnemyId, out var v) ? v : (ushort)0;
        var forcedTicks = client.LatestEnemyForcedTicks.TryGetValue(nearestEnemyId, out var ft) ? ft : (byte)0;
        var stacks = client.LatestEnemyPrimaryStatusStacks.TryGetValue(nearestEnemyId, out var s) ? s : (byte)0;
        return $"Aggro enemy {nearestEnemyId}: target={target} threat={threat} forced={forcedTicks} stacks={stacks}";
    }
}
}
