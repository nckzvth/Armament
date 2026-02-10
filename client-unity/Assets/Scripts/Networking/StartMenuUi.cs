#nullable enable
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Armament.Client.Networking;

public sealed class StartMenuUi : MonoBehaviour
{
    private const string PrefHost = "armament.menu.host";
    private const string PrefPort = "armament.menu.port";
    private const string PrefAccountSubject = "armament.menu.account_subject";
    private const string PrefAccountDisplayName = "armament.menu.account_display";
    private const string PrefCharacterName = "armament.menu.character_name";
    private const string PrefSlot = "armament.menu.slot";

    private UdpGameClient? client;
    private string host = "127.0.0.1";
    private string port = "9000";
    private string accountSubject = "local:dev-account";
    private string accountDisplayName = "DevAccount";
    private string characterName = "Warrior";
    private int slotIndex;
    private bool isConnecting;
    private string statusText = "Select a character and connect.";

    private void Awake()
    {
        client = GetComponent<UdpGameClient>() ?? FindFirstObjectByType<UdpGameClient>();
        host = PlayerPrefs.GetString(PrefHost, host);
        port = PlayerPrefs.GetString(PrefPort, port);
        accountSubject = PlayerPrefs.GetString(PrefAccountSubject, accountSubject);
        accountDisplayName = PlayerPrefs.GetString(PrefAccountDisplayName, accountDisplayName);
        characterName = PlayerPrefs.GetString(PrefCharacterName, characterName);
        slotIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefSlot, slotIndex), 0, 3);

        if (client is not null)
        {
            if (string.IsNullOrWhiteSpace(accountSubject))
            {
                accountSubject = client.AccountSubject;
            }

            if (string.IsNullOrWhiteSpace(accountDisplayName))
            {
                accountDisplayName = client.AccountDisplayName;
            }
        }

        ApplySlotPresetName();
    }

    private void OnGUI()
    {
        if (client is null || client.IsJoined)
        {
            return;
        }

        var panelRect = new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.5f - 180f, 400f, 360f);
        GUI.Box(panelRect, "Armament Start Menu");

        var x = panelRect.x + 14f;
        var y = panelRect.y + 34f;
        var width = panelRect.width - 28f;

        GUI.Label(new Rect(x, y, width, 20f), "Server Host");
        y += 20f;
        host = GUI.TextField(new Rect(x, y, width, 24f), host);
        y += 30f;

        GUI.Label(new Rect(x, y, width, 20f), "Server Port");
        y += 20f;
        port = GUI.TextField(new Rect(x, y, width, 24f), port);
        y += 30f;

        GUI.Label(new Rect(x, y, width, 20f), "Account Subject");
        y += 20f;
        accountSubject = GUI.TextField(new Rect(x, y, width, 24f), accountSubject);
        y += 30f;

        GUI.Label(new Rect(x, y, width, 20f), "Account Display Name");
        y += 20f;
        accountDisplayName = GUI.TextField(new Rect(x, y, width, 24f), accountDisplayName);
        y += 30f;

        GUI.Label(new Rect(x, y, width, 20f), "Character Name");
        y += 20f;
        characterName = GUI.TextField(new Rect(x, y, width, 24f), characterName);
        y += 30f;

        GUI.Label(new Rect(x, y, width, 20f), "Character Slot");
        y += 20f;
        var buttonWidth = (width - 24f) / 4f;
        for (var i = 0; i < 4; i++)
        {
            var rect = new Rect(x + i * (buttonWidth + 8f), y, buttonWidth, 24f);
            if (GUI.Toggle(rect, slotIndex == i, $"Slot {i}"))
            {
                if (slotIndex != i)
                {
                    slotIndex = i;
                    ApplySlotPresetName();
                }
            }
        }

        y += 34f;
        using (new EditorGuiDisabledScope(isConnecting))
        {
            if (GUI.Button(new Rect(x, y, width, 30f), isConnecting ? "Connecting..." : "Connect"))
            {
                _ = ConnectAsync();
            }
        }

        y += 34f;
        GUI.Label(new Rect(x, y, width, 40f), statusText);
    }

    private async Task ConnectAsync()
    {
        if (client is null || isConnecting)
        {
            return;
        }

        if (!int.TryParse(port, out var parsedPort))
        {
            statusText = "Invalid port.";
            return;
        }

        isConnecting = true;
        statusText = "Connecting...";
        try
        {
            client.Configure(
                host.Trim(),
                parsedPort,
                characterName.Trim(),
                accountSubject.Trim(),
                slotIndex,
                accountDisplayName.Trim());
            await client.ConnectAsync();
            SavePreferences();
            statusText = "Connected. Waiting for join...";
        }
        catch (Exception ex)
        {
            statusText = $"Connect failed: {ex.Message}";
        }
        finally
        {
            isConnecting = false;
        }
    }

    private void ApplySlotPresetName()
    {
        if (string.IsNullOrWhiteSpace(characterName) || characterName.StartsWith("Character ", StringComparison.Ordinal))
        {
            characterName = slotIndex switch
            {
                0 => "Warrior",
                1 => "Mage",
                2 => "Ranger",
                _ => $"Character {slotIndex + 1}"
            };
        }
    }

    private void SavePreferences()
    {
        PlayerPrefs.SetString(PrefHost, host);
        PlayerPrefs.SetString(PrefPort, port);
        PlayerPrefs.SetString(PrefAccountSubject, accountSubject);
        PlayerPrefs.SetString(PrefAccountDisplayName, accountDisplayName);
        PlayerPrefs.SetString(PrefCharacterName, characterName);
        PlayerPrefs.SetInt(PrefSlot, slotIndex);
        PlayerPrefs.Save();
    }

    private readonly struct EditorGuiDisabledScope : IDisposable
    {
        private readonly bool wasEnabled;

        public EditorGuiDisabledScope(bool disabled)
        {
            wasEnabled = GUI.enabled;
            GUI.enabled = !disabled;
        }

        public void Dispose()
        {
            GUI.enabled = wasEnabled;
        }
    }
}
