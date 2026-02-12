#nullable enable
using System;
using System.Threading.Tasks;
using Armament.SharedSim.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Armament.Client.Networking
{

public sealed class StartMenuUi : MonoBehaviour
{
    private const int MaxSlots = 6;
    private const string PrefHost = "armament.menu.host";
    private const string PrefPort = "armament.menu.port";
    private const string PrefAccountSubject = "armament.menu.account_subject";
    private const string PrefAccountDisplayName = "armament.menu.account_display";
    private const string PrefCharacterName = "armament.menu.character_name";
    private const string PrefSlot = "armament.menu.slot";
    private const string PrefBaseClass = "armament.menu.base_class";
    private const string PrefSpec = "armament.menu.spec";

    private enum UiScreen
    {
        Login,
        CharacterSelect,
        CharacterCreation,
        Settings
    }

    private UdpGameClient? client;
    private UiScreen screen = UiScreen.Login;

    private string host = "127.0.0.1";
    private string port = "9000";
    private string accountSubject = "local:dev-account";
    private string accountDisplayName = "DevAccount";
    private string loginUsername = "DevAccount";
    private string loginPassword = string.Empty;
    private string characterName = "Warrior";
    private string baseClassId = "bastion";
    private string specId = "spec.bastion.bulwark";
    private int slotIndex;
    private bool isConnecting;
    private bool pauseMenuOpen;
    private bool settingsFromPauseMenu;
    private int creationTargetSlot = -1;
    private string statusText = "Log in to continue.";
    private GUIStyle? titleStyle;
    private GUIStyle? subtitleStyle;

    private void Awake()
    {
        client = GetComponent<UdpGameClient>() ?? FindFirstObjectByType<UdpGameClient>();
        client?.SetAutoConnect(false);
        host = PlayerPrefs.GetString(PrefHost, host);
        port = PlayerPrefs.GetString(PrefPort, port);
        accountSubject = PlayerPrefs.GetString(PrefAccountSubject, accountSubject);
        accountDisplayName = PlayerPrefs.GetString(PrefAccountDisplayName, accountDisplayName);
        characterName = PlayerPrefs.GetString(PrefCharacterName, characterName);
        slotIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefSlot, slotIndex), 0, MaxSlots - 1);
        baseClassId = PlayerPrefs.GetString(PrefBaseClass, baseClassId);
        specId = PlayerPrefs.GetString(PrefSpec, specId);
        loginUsername = accountDisplayName;

        if (!string.IsNullOrWhiteSpace(accountSubject))
        {
            var subject = accountSubject.Trim();
            if (subject.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            {
                loginUsername = subject[6..];
            }
        }

        _ = TryLoadSlotSelection();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard is null || client is null || !client.IsJoined)
        {
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            pauseMenuOpen = !pauseMenuOpen;
        }
    }

    private void OnGUI()
    {
        if (client is null)
        {
            return;
        }

        EnsureStyles();

        if (client.IsJoined && screen != UiScreen.Settings)
        {
            if (pauseMenuOpen)
            {
                DrawPauseMenu();
            }

            return;
        }

        switch (screen)
        {
            case UiScreen.Login:
                DrawLoginScreen();
                break;
            case UiScreen.CharacterSelect:
                DrawCharacterSelectScreen();
                break;
            case UiScreen.CharacterCreation:
                DrawCharacterCreationScreen();
                break;
            case UiScreen.Settings:
                DrawSettingsScreen();
                break;
        }
    }

    private void DrawLoginScreen()
    {
        var panelRect = new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.5f - 200f, 520f, 400f);
        GUI.Box(panelRect, string.Empty);
        GUI.Label(new Rect(panelRect.x, panelRect.y + 20f, panelRect.width, 36f), "ARMAMENT", titleStyle!);
        GUI.Label(new Rect(panelRect.x, panelRect.y + 58f, panelRect.width, 24f), "LOGIN", subtitleStyle!);

        var x = panelRect.x + 42f;
        var y = panelRect.y + 108f;
        var width = panelRect.width - 84f;

        GUI.Label(new Rect(x, y, width, 20f), "USERNAME");
        y += 22f;
        loginUsername = GUI.TextField(new Rect(x, y, width, 30f), loginUsername);
        y += 40f;

        GUI.Label(new Rect(x, y, width, 20f), "PASSWORD");
        y += 22f;
        loginPassword = GUI.PasswordField(new Rect(x, y, width, 30f), loginPassword, '*');
        y += 46f;

        if (GUI.Button(new Rect(x, y, width, 34f), "LOG IN"))
        {
            PerformLogin(createAccount: false);
        }

        y += 42f;
        if (GUI.Button(new Rect(x, y, width, 28f), "CREATE ACCOUNT"))
        {
            PerformLogin(createAccount: true);
        }

        y += 36f;
        GUI.Label(new Rect(x, y, width, 36f), statusText);
    }

    private void DrawCharacterSelectScreen()
    {
        var panelRect = new Rect(Screen.width * 0.5f - 420f, Screen.height * 0.5f - 260f, 840f, 520f);
        GUI.Box(panelRect, string.Empty);
        GUI.Label(new Rect(panelRect.x, panelRect.y + 14f, panelRect.width, 36f), "CHARACTER SELECT", titleStyle!);

        var x = panelRect.x + 22f;
        var y = panelRect.y + 66f;

        GUI.Label(new Rect(x, y, 280f, 20f), $"Account: {accountDisplayName} ({accountSubject})");
        y += 26f;

        var filledSlots = GetFilledSlots();
        var nextEmptySlot = GetNextEmptySlot();
        var row = 0;

        for (var i = 0; i < filledSlots.Count; i++)
        {
            var filled = filledSlots[i];
            var rect = new Rect(x, y + row * 42f, 300f, 34f);
            var summary = GetSlotSummary(filled.Slot);
            if (GUI.Toggle(rect, slotIndex == filled.Slot, summary))
            {
                if (slotIndex != filled.Slot)
                {
                    slotIndex = filled.Slot;
                    _ = TryLoadSlotSelection();
                }
            }

            row++;
        }

        if (nextEmptySlot >= 0)
        {
            var createRect = new Rect(x, y + row * 42f, 300f, 34f);
            if (GUI.Button(createRect, $"Slot {nextEmptySlot}: + Create Character"))
            {
                slotIndex = nextEmptySlot;
                characterName = GetDefaultNameForSlot(slotIndex);
                baseClassId = "bastion";
                specId = ClassSpecCatalog.NormalizeSpecForClass(baseClassId, null);
                creationTargetSlot = nextEmptySlot;
                screen = UiScreen.CharacterCreation;
                statusText = "Choose class/spec and create.";
            }

            row++;
        }

        var detailX = panelRect.x + 350f;
        var detailY = panelRect.y + 84f;
        var detailW = panelRect.width - 372f;
        var slotEmpty = IsSlotEmpty(slotIndex);
        GUI.Box(new Rect(detailX, detailY, detailW, 320f), string.Empty);
        GUI.Label(new Rect(detailX + 12f, detailY + 12f, detailW - 24f, 24f), $"Slot {slotIndex}");
        GUI.Label(new Rect(detailX + 12f, detailY + 42f, detailW - 24f, 24f), $"Name: {(slotEmpty ? "<empty>" : characterName)}");
        GUI.Label(new Rect(detailX + 12f, detailY + 68f, detailW - 24f, 24f), $"Class: {(slotEmpty ? "<none>" : baseClassId)}");
        GUI.Label(new Rect(detailX + 12f, detailY + 94f, detailW - 24f, 24f), $"Spec: {(slotEmpty ? "<none>" : specId)}");

        GUI.Label(new Rect(detailX + 12f, detailY + 132f, 120f, 20f), "Server Host");
        host = GUI.TextField(new Rect(detailX + 12f, detailY + 154f, detailW - 24f, 26f), host);
        GUI.Label(new Rect(detailX + 12f, detailY + 186f, 120f, 20f), "Server Port");
        port = GUI.TextField(new Rect(detailX + 12f, detailY + 208f, detailW - 24f, 26f), port);

        var btnY = detailY + 246f;
        using (new EditorGuiDisabledScope(isConnecting || slotEmpty))
        {
            var playLabel = slotEmpty ? "PLAY (CREATE CHARACTER FIRST)" : (isConnecting ? "CONNECTING..." : "PLAY");
            if (GUI.Button(new Rect(detailX + 12f, btnY, detailW - 24f, 34f), playLabel))
            {
                _ = ConnectAsync();
            }
        }

        var bottomY = panelRect.y + panelRect.height - 74f;
        using (new EditorGuiDisabledScope(slotEmpty))
        {
            if (GUI.Button(new Rect(x, bottomY, 160f, 32f), "DELETE"))
            {
                DeleteCurrentSlot();
            }
        }

        GUI.Label(new Rect(x + 172f, bottomY + 6f, 160f, 24f), nextEmptySlot >= 0 ? "Use + Create Character" : "All slots full");

        if (GUI.Button(new Rect(detailX, bottomY, 160f, 32f), "LOGOUT"))
        {
            screen = UiScreen.Login;
            statusText = "Logged out.";
        }

        if (GUI.Button(new Rect(detailX + 172f, bottomY, 160f, 32f), "BACK"))
        {
            screen = UiScreen.Login;
        }

        var footerText = slotEmpty
            ? "Selected slot is empty. Use CREATE NEW before PLAY."
            : statusText;
        GUI.Label(new Rect(detailX, panelRect.y + panelRect.height - 36f, detailW, 24f), footerText);
    }

    private void DrawCharacterCreationScreen()
    {
        var panelRect = new Rect(Screen.width * 0.5f - 420f, Screen.height * 0.5f - 260f, 840f, 520f);
        GUI.Box(panelRect, string.Empty);
        GUI.Label(new Rect(panelRect.x, panelRect.y + 14f, panelRect.width, 36f), "CHARACTER CREATION", titleStyle!);

        var x = panelRect.x + 30f;
        var y = panelRect.y + 80f;
        var nextEmptySlot = GetNextEmptySlot();
        var targetSlot = creationTargetSlot >= 0 ? creationTargetSlot : nextEmptySlot;
        var slotBlocked = targetSlot < 0;
        GUI.Label(new Rect(x, y, 360f, 20f), $"Slot: {(targetSlot < 0 ? "N/A" : targetSlot)}");
        y += 26f;
        GUI.Label(new Rect(x, y, 360f, 20f), "Character Name");
        y += 22f;
        characterName = GUI.TextField(new Rect(x, y, 320f, 30f), characterName);
        y += 44f;
        GUI.Label(new Rect(x, y, 360f, 20f), "Base Class");
        y += 24f;

        var classes = ClassSpecCatalog.BaseClasses;
        for (var i = 0; i < classes.Length; i++)
        {
            var row = i / 2;
            var col = i % 2;
            var rect = new Rect(x + col * 164f, y + row * 34f, 156f, 28f);
            var label = char.ToUpperInvariant(classes[i][0]) + classes[i][1..];
            if (GUI.Toggle(rect, string.Equals(baseClassId, classes[i], StringComparison.OrdinalIgnoreCase), label))
            {
                if (!string.Equals(baseClassId, classes[i], StringComparison.OrdinalIgnoreCase))
                {
                    baseClassId = classes[i];
                    specId = ClassSpecCatalog.GetSpecsForClass(baseClassId)[0];
                }
            }
        }

        var specX = panelRect.x + 420f;
        var specY = panelRect.y + 126f;
        GUI.Label(new Rect(specX, specY - 24f, 360f, 20f), "Spec");
        var specs = ClassSpecCatalog.GetSpecsForClass(baseClassId);
        for (var i = 0; i < specs.Count; i++)
        {
            var rect = new Rect(specX, specY + i * 34f, 330f, 28f);
            var label = specs[i].Replace("spec.", string.Empty);
            if (GUI.Toggle(rect, string.Equals(specId, specs[i], StringComparison.OrdinalIgnoreCase), label))
            {
                specId = specs[i];
            }
        }

        var btnY = panelRect.y + panelRect.height - 72f;
        if (slotBlocked)
        {
            GUI.Box(new Rect(panelRect.x + 30f, panelRect.y + panelRect.height - 122f, 374f, 42f), string.Empty);
            GUI.Label(new Rect(panelRect.x + 38f, panelRect.y + panelRect.height - 106f, 360f, 20f), "No empty slots available. Delete a character first.");
        }

        if (GUI.Button(new Rect(panelRect.x + 30f, btnY, 180f, 34f), "CANCEL"))
        {
            screen = UiScreen.CharacterSelect;
            creationTargetSlot = -1;
            _ = TryLoadSlotSelection();
        }

        var canCreate = !slotBlocked && !string.IsNullOrWhiteSpace(characterName) && targetSlot == nextEmptySlot;
        using (new EditorGuiDisabledScope(!canCreate))
        {
            if (GUI.Button(new Rect(panelRect.x + 224f, btnY, 180f, 34f), "CREATE"))
            {
                SaveSlotSelection(targetSlot);
                SavePreferences();
                slotIndex = targetSlot;
                creationTargetSlot = -1;
                screen = UiScreen.CharacterSelect;
                statusText = $"Created slot {slotIndex}: {characterName} ({baseClassId})";
            }
        }

        if (slotBlocked)
        {
            GUI.Label(new Rect(panelRect.x + 418f, panelRect.y + panelRect.height - 68f, 380f, 20f), "Delete an existing character to continue.");
        }
        else if (targetSlot != nextEmptySlot)
        {
            GUI.Label(new Rect(panelRect.x + 418f, panelRect.y + panelRect.height - 68f, 380f, 20f), "Target slot changed. Re-open creation from character select.");
        }
        else if (string.IsNullOrWhiteSpace(characterName))
        {
            GUI.Label(new Rect(panelRect.x + 418f, panelRect.y + panelRect.height - 68f, 380f, 20f), "Character name is required.");
        }
    }

    private void DrawPauseMenu()
    {
        var rect = new Rect(Screen.width * 0.5f - 170f, Screen.height * 0.5f - 140f, 340f, 280f);
        GUI.Box(rect, string.Empty);
        GUI.Label(new Rect(rect.x, rect.y + 14f, rect.width, 28f), "ARMAMENT", titleStyle!);

        var x = rect.x + 20f;
        var y = rect.y + 54f;
        var w = rect.width - 40f;

        if (GUI.Button(new Rect(x, y, w, 34f), "RESUME"))
        {
            pauseMenuOpen = false;
        }

        y += 42f;
        if (GUI.Button(new Rect(x, y, w, 34f), "RETURN TO CHARACTER SELECT"))
        {
            _ = ReturnToCharacterSelectAsync();
        }

        y += 42f;
        if (GUI.Button(new Rect(x, y, w, 34f), "LOGOUT"))
        {
            _ = LogoutAsync();
        }

        y += 42f;
        if (GUI.Button(new Rect(x, y, w, 34f), "SETTINGS"))
        {
            pauseMenuOpen = false;
            settingsFromPauseMenu = true;
            screen = UiScreen.Settings;
        }

        y += 42f;
        if (GUI.Button(new Rect(x, y, w, 34f), "EXIT GAME"))
        {
            ExitGame();
        }
    }

    private void DrawSettingsScreen()
    {
        var panelRect = new Rect(Screen.width * 0.5f - 280f, Screen.height * 0.5f - 180f, 560f, 360f);
        GUI.Box(panelRect, string.Empty);
        GUI.Label(new Rect(panelRect.x, panelRect.y + 16f, panelRect.width, 30f), "SETTINGS", titleStyle!);

        var x = panelRect.x + 28f;
        var y = panelRect.y + 68f;
        var width = panelRect.width - 56f;

        GUI.Label(new Rect(x, y, width, 20f), "Network Host");
        y += 22f;
        host = GUI.TextField(new Rect(x, y, width, 28f), host);
        y += 36f;

        GUI.Label(new Rect(x, y, width, 20f), "Network Port");
        y += 22f;
        port = GUI.TextField(new Rect(x, y, width, 28f), port);
        y += 40f;

        GUI.Box(new Rect(x, y, width, 82f), string.Empty);
        GUI.Label(new Rect(x + 10f, y + 10f, width - 20f, 20f), "Audio/Graphics/Controls options will be added here.");
        GUI.Label(new Rect(x + 10f, y + 34f, width - 20f, 20f), "This panel is now a stable extension point for final UX.");

        var buttonY = panelRect.y + panelRect.height - 50f;
        if (GUI.Button(new Rect(x, buttonY, 180f, 30f), "SAVE"))
        {
            SavePreferences();
            statusText = "Settings saved.";
            if (settingsFromPauseMenu && client is not null && client.IsJoined)
            {
                pauseMenuOpen = true;
            }
        }

        if (GUI.Button(new Rect(panelRect.x + panelRect.width - 208f, buttonY, 180f, 30f), "BACK"))
        {
            if (client is not null && client.IsJoined)
            {
                screen = UiScreen.CharacterSelect;
                pauseMenuOpen = true;
            }
            else
            {
                screen = UiScreen.CharacterSelect;
            }

            settingsFromPauseMenu = false;
        }
    }

    private void PerformLogin(bool createAccount)
    {
        var trimmed = loginUsername.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            statusText = "Username is required.";
            return;
        }

        accountDisplayName = trimmed;
        accountSubject = $"local:{CharacterSlotStorage.NormalizeSubject(trimmed)}";
        _ = TryLoadSlotSelection();
        creationTargetSlot = -1;
        screen = UiScreen.CharacterSelect;
        statusText = createAccount ? "Account created locally." : "Logged in.";
        SavePreferences();
    }

    private async Task ReturnToCharacterSelectAsync()
    {
        if (client is null)
        {
            return;
        }

        pauseMenuOpen = false;
        settingsFromPauseMenu = false;
        creationTargetSlot = -1;
        await client.DisconnectAsync();
        screen = UiScreen.CharacterSelect;
        statusText = "Returned to character select.";
    }

    private async Task LogoutAsync()
    {
        if (client is not null)
        {
            await client.DisconnectAsync();
        }

        pauseMenuOpen = false;
        settingsFromPauseMenu = false;
        creationTargetSlot = -1;
        screen = UiScreen.Login;
        statusText = "Logged out.";
    }

    private async Task ConnectAsync()
    {
        if (client is null || isConnecting)
        {
            return;
        }

        if (IsSlotEmpty(slotIndex))
        {
            statusText = "Cannot play: selected slot is empty. Create a character first.";
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
                accountDisplayName.Trim(),
                baseClassId,
                specId);
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

    private void DeleteCurrentSlot()
    {
        var deletedSlot = slotIndex;
        DeleteSlotAndCompact(deletedSlot);
        PlayerPrefs.Save();

        var filledSlots = GetFilledSlots();
        if (filledSlots.Count == 0)
        {
            slotIndex = 0;
            characterName = string.Empty;
            baseClassId = "bastion";
            specId = "spec.bastion.bulwark";
        }
        else
        {
            slotIndex = Mathf.Clamp(deletedSlot, 0, filledSlots.Count - 1);
            slotIndex = filledSlots[slotIndex].Slot;
            _ = TryLoadSlotSelection();
        }

        statusText = $"Deleted slot {deletedSlot}.";
    }

    private void SavePreferences()
    {
        PlayerPrefs.SetString(PrefHost, host);
        PlayerPrefs.SetString(PrefPort, port);
        PlayerPrefs.SetString(PrefAccountSubject, accountSubject);
        PlayerPrefs.SetString(PrefAccountDisplayName, accountDisplayName);
        PlayerPrefs.SetString(PrefCharacterName, characterName);
        PlayerPrefs.SetInt(PrefSlot, slotIndex);
        PlayerPrefs.SetString(PrefBaseClass, baseClassId);
        PlayerPrefs.SetString(PrefSpec, specId);
        PlayerPrefs.Save();
    }

    private bool TryLoadSlotSelection()
    {
        if (!CharacterSlotStorage.TryLoadSlot(accountSubject, slotIndex, out var name, out var loadedClass, out var loadedSpec))
        {
            characterName = string.Empty;
            baseClassId = "bastion";
            specId = "spec.bastion.bulwark";
            return false;
        }

        characterName = name;
        baseClassId = loadedClass;
        specId = loadedSpec;
        return true;
    }

    private void SaveSlotSelection(int slot)
    {
        if (string.IsNullOrWhiteSpace(characterName))
        {
            return;
        }

        CharacterSlotStorage.SaveSlot(accountSubject, slot, characterName.Trim(), baseClassId, specId);
        baseClassId = ClassSpecCatalog.NormalizeBaseClass(baseClassId);
        specId = ClassSpecCatalog.NormalizeSpecForClass(baseClassId, specId);
        PlayerPrefs.Save();
    }

    private string GetSlotSummary(int slot)
    {
        if (!CharacterSlotStorage.TryLoadSlot(accountSubject, slot, out var name, out var baseClass, out var spec))
        {
            return $"Slot {slot}: Empty";
        }

        return $"Slot {slot}: {name} ({baseClass} / {spec.Replace("spec.", string.Empty)})";
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

    private bool IsSlotEmpty(int slot)
    {
        return CharacterSlotStorage.IsSlotEmpty(accountSubject, slot);
    }

    private void DeleteSlotAndCompact(int deletedSlot)
    {
        CharacterSlotStorage.DeleteSlotAndCompact(accountSubject, deletedSlot, MaxSlots);
    }

    private System.Collections.Generic.List<SlotInfo> GetFilledSlots()
    {
        var result = new System.Collections.Generic.List<SlotInfo>();
        var filled = CharacterSlotStorage.GetFilledSlots(accountSubject, MaxSlots);
        for (var i = 0; i < filled.Count; i++)
        {
            result.Add(new SlotInfo(filled[i]));
        }

        return result;
    }

    private int GetNextEmptySlot()
    {
        return CharacterSlotStorage.GetNextEmptySlot(accountSubject, MaxSlots);
    }

    private void EnsureStyles()
    {
        if (titleStyle is null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                fontStyle = FontStyle.Bold
            };
        }

        if (subtitleStyle is null)
        {
            subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
        }
    }

    private static void ExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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

    private readonly struct SlotInfo
    {
        public SlotInfo(int slot)
        {
            Slot = slot;
        }

        public int Slot { get; }
    }
}
}
