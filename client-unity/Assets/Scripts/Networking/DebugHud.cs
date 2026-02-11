#nullable enable
using UnityEngine;

namespace Armament.Client.Networking;

public sealed class DebugHud : MonoBehaviour
{
    private UdpGameClient? client;

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

        var panelRect = new Rect(10, 10, 540, 352);
        GUI.Box(panelRect, string.Empty);

        var y = panelRect.y + 8f;
        var x = panelRect.x + 8f;
        const float lineHeight = 22f;
        const float width = 520f;

        GUI.Label(new Rect(x, y, width, lineHeight), "Armament Phase 3 Debug HUD"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), $"Local Entity: {client.LocalEntityId}"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), $"Account: {client.AccountSubject} | Slot: {client.CharacterSlot}"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), $"Class/Spec: {client.BaseClassId} | {client.SpecId}"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), $"Zone: {client.CurrentZone} (Instance {client.CurrentInstanceId})"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), $"HP: {client.LocalHealth}"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), $"Builder: {client.LocalBuilderResource}"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), $"Spender: {client.LocalSpenderResource}"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), $"Currency: {client.LocalCurrency}"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), $"Visible Entities: {client.RenderEntities.Count}"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), $"Drop Names: {(client.ShowLootNames ? "ON" : "OFF")} (Option/Alt toggle)"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), "Ability Runner: ENABLED (server profile)"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), "Controls: WASD | LMB fast | RMB heavy | Shift block"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), "Skills: E/R/Q/T + 1/2/3/4 | Z loot | Gold auto-loot"); y += lineHeight;
        GUI.Label(new Rect(x, y, width, lineHeight), "Interact: [F] Dungeon/NPC | Return: [H] Overworld");
    }
}
