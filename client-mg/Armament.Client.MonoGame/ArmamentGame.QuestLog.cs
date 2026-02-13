using System;
using System.Collections.Generic;
using System.Linq;
using Armament.SharedSim.Protocol;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Armament.Client.MonoGame;

public sealed partial class ArmamentGame
{
    private static readonly string[] ZoneSortOrder =
    {
        "cp", "bt", "gg", "ig", "ma", "cc", "hub"
    };
    private static readonly Dictionary<string, string> QuestZoneOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["quest.act1.contract_board"] = "cp"
    };

    private void HandleQuestLogInput(KeyboardState keyboard, MouseState mouse)
    {
        if (WasPressed(keyboard, previousKeyboard, Keys.OemOpenBrackets))
        {
            selectedQuestActIndex = Math.Max(0, selectedQuestActIndex - 1);
            selectedQuestZoneIndex = 0;
            selectedQuestIndex = 0;
        }
        else if (WasPressed(keyboard, previousKeyboard, Keys.OemCloseBrackets))
        {
            selectedQuestActIndex = Math.Min(Math.Max(0, questLogActs.Count - 1), selectedQuestActIndex + 1);
            selectedQuestZoneIndex = 0;
            selectedQuestIndex = 0;
        }

        if (WasPressed(keyboard, previousKeyboard, Keys.Left))
        {
            selectedQuestZoneIndex = Math.Max(0, selectedQuestZoneIndex - 1);
            selectedQuestIndex = 0;
        }
        else if (WasPressed(keyboard, previousKeyboard, Keys.Right))
        {
            selectedQuestZoneIndex++;
            selectedQuestIndex = 0;
        }

        if (WasPressed(keyboard, previousKeyboard, Keys.Up))
        {
            selectedQuestIndex = Math.Max(0, selectedQuestIndex - 1);
        }
        else if (WasPressed(keyboard, previousKeyboard, Keys.Down))
        {
            selectedQuestIndex++;
        }

        if (WasPressed(mouse, previousMouse, true))
        {
            HandleQuestLogMouseClick(mouse.Position);
        }
    }

    private void HandleQuestLogMouseClick(Point click)
    {
        if (questLogActs.Count == 0)
        {
            return;
        }

        var panel = GetQuestLogPanelRect();
        if (!panel.Contains(click))
        {
            return;
        }

        for (var i = 0; i < questLogActs.Count; i++)
        {
            if (GetActTabRect(panel, i).Contains(click))
            {
                selectedQuestActIndex = i;
                selectedQuestZoneIndex = 0;
                selectedQuestIndex = 0;
                return;
            }
        }

        if (!TryGetSelectedAct(out var act) || act.Zones.Count == 0)
        {
            return;
        }

        var iconRects = BuildZoneIconRects(panel, act.Zones.Count);
        for (var i = 0; i < act.Zones.Count && i < iconRects.Count; i++)
        {
            if (iconRects[i].Contains(click))
            {
                selectedQuestZoneIndex = i;
                selectedQuestIndex = 0;
                return;
            }
        }

        if (!TryGetSelectedZone(out var zone) || zone.Quests.Count == 0)
        {
            return;
        }

        for (var i = 0; i < zone.Quests.Count; i++)
        {
            if (GetQuestRowRect(panel, i).Contains(click))
            {
                selectedQuestIndex = i;
                return;
            }
        }
    }

    private void DrawQuestLogPanel(SpriteBatch batch, SpriteFont font)
    {
        var panel = GetQuestLogPanelRect();
        batch.Draw(pixel!, panel, new Color(13, 13, 18, 238));
        DrawOutline(batch, panel, new Color(166, 164, 154, 240));

        var header = new Rectangle(panel.X + 10, panel.Y + 10, panel.Width - 20, 36);
        batch.Draw(pixel!, header, new Color(28, 28, 36, 255));
        DrawOutline(batch, header, new Color(128, 126, 118, 220));
        batch.DrawString(font, "Quest Log", new Vector2(header.X + 12, header.Y + 10), new Color(229, 219, 191));

        if (questLogActs.Count == 0)
        {
            batch.DrawString(font, "No tracked objectives.", new Vector2(panel.X + 18, panel.Y + 60), new Color(210, 215, 228));
            return;
        }

        selectedQuestActIndex = Math.Clamp(selectedQuestActIndex, 0, questLogActs.Count - 1);

        var tabY = panel.Y + 56;
        for (var i = 0; i < questLogActs.Count; i++)
        {
            var tab = GetActTabRect(panel, i);
            var active = i == selectedQuestActIndex;
            batch.Draw(pixel!, tab, active ? new Color(76, 73, 65, 255) : new Color(36, 35, 41, 255));
            DrawOutline(batch, tab, active ? new Color(194, 184, 157, 235) : new Color(102, 100, 112, 220));
            batch.DrawString(font, questLogActs[i].Title, new Vector2(tab.X + 9, tab.Y + 4), active ? new Color(248, 242, 220) : new Color(184, 188, 204));
        }

        if (!TryGetSelectedAct(out var selectedAct) || selectedAct.Zones.Count == 0)
        {
            batch.DrawString(font, "No zones in selected act.", new Vector2(panel.X + 18, panel.Y + 94), new Color(210, 215, 228));
            return;
        }

        selectedQuestZoneIndex = Math.Clamp(selectedQuestZoneIndex, 0, selectedAct.Zones.Count - 1);

        var iconGrid = new Rectangle(panel.X + 12, panel.Y + 92, panel.Width - 24, 128);
        batch.Draw(pixel!, iconGrid, new Color(20, 19, 26, 245));
        DrawOutline(batch, iconGrid, new Color(103, 101, 112, 220));

        var iconRects = BuildZoneIconRects(panel, selectedAct.Zones.Count);
        for (var i = 0; i < selectedAct.Zones.Count && i < iconRects.Count; i++)
        {
            var zone = selectedAct.Zones[i];
            var icon = iconRects[i];
            var active = i == selectedQuestZoneIndex;
            var zoneColor = ResolveZoneStateColor(zone.StateCode);

            batch.Draw(pixel!, icon, active ? new Color(74, 70, 60, 255) : new Color(30, 30, 38, 255));
            DrawOutline(batch, icon, active ? new Color(224, 205, 152, 240) : new Color(106, 106, 122, 220));

            var inner = new Rectangle(icon.X + 8, icon.Y + 8, icon.Width - 16, icon.Height - 16);
            batch.Draw(pixel!, inner, zoneColor * 0.5f);
            DrawOutline(batch, inner, zoneColor);

            var keyText = zone.Key.ToUpperInvariant();
            var keySize = font.MeasureString(keyText);
            batch.DrawString(font, keyText, new Vector2(icon.Center.X - keySize.X * 0.5f, icon.Bottom - 22), new Color(240, 238, 228));
        }

        if (!TryGetSelectedZone(out var selectedZone) || selectedZone.Quests.Count == 0)
        {
            batch.DrawString(font, "No quests for selected zone.", new Vector2(panel.X + 18, iconGrid.Bottom + 10), new Color(210, 215, 228));
            return;
        }

        selectedQuestIndex = Math.Clamp(selectedQuestIndex, 0, selectedZone.Quests.Count - 1);

        var listPane = new Rectangle(panel.X + 12, iconGrid.Bottom + 10, 240, panel.Bottom - iconGrid.Bottom - 22);
        var detailPane = new Rectangle(listPane.Right + 10, iconGrid.Bottom + 10, panel.Right - (listPane.Right + 22), panel.Bottom - iconGrid.Bottom - 22);

        batch.Draw(pixel!, listPane, new Color(16, 16, 22, 248));
        DrawOutline(batch, listPane, new Color(103, 101, 112, 220));
        batch.DrawString(font, selectedZone.Title, new Vector2(listPane.X + 8, listPane.Y + 8), new Color(234, 225, 196));

        var rowsStartY = listPane.Y + 34;
        for (var i = 0; i < selectedZone.Quests.Count; i++)
        {
            var row = GetQuestRowRect(panel, i);
            if (row.Bottom > listPane.Bottom - 6)
            {
                break;
            }

            var active = i == selectedQuestIndex;
            var quest = selectedZone.Quests[i];
            batch.Draw(pixel!, row, active ? new Color(62, 58, 52, 255) : new Color(26, 26, 34, 240));
            DrawOutline(batch, row, active ? new Color(220, 198, 142, 220) : new Color(88, 90, 104, 200));
            batch.DrawString(font, quest.Title, new Vector2(row.X + 8, row.Y + 6), new Color(224, 228, 238));
            batch.DrawString(font, quest.StateLabel, new Vector2(row.Right - 76, row.Y + 6), ResolveQuestStateColor(quest.StateCode));
        }

        batch.Draw(pixel!, detailPane, new Color(16, 16, 22, 248));
        DrawOutline(batch, detailPane, new Color(103, 101, 112, 220));

        var selectedQuest = selectedZone.Quests[selectedQuestIndex];
        batch.DrawString(font, selectedQuest.Title, new Vector2(detailPane.X + 10, detailPane.Y + 10), new Color(238, 228, 198));
        batch.DrawString(font, selectedQuest.StateLabel, new Vector2(detailPane.X + 10, detailPane.Y + 34), ResolveQuestStateColor(selectedQuest.StateCode));
        batch.DrawString(font, $"Objectives: {selectedQuest.CompletedObjectives}/{selectedQuest.TotalObjectives}", new Vector2(detailPane.X + 10, detailPane.Y + 56), new Color(210, 216, 230));

        var objectiveY = detailPane.Y + 86;
        var maxLines = Math.Max(3, (detailPane.Bottom - objectiveY - 8) / 20);
        for (var i = 0; i < selectedQuest.Objectives.Count && i < maxLines; i++)
        {
            var objective = selectedQuest.Objectives[i];
            var marker = objective.StateCode == 2 ? "[x]" : "[ ]";
            var text = $"{marker} {objective.Label} ({objective.Current}/{objective.Required})";
            batch.DrawString(font, text, new Vector2(detailPane.X + 10, objectiveY + (i * 20)), ResolveObjectiveStateColor(objective.StateCode));
        }
    }

    private Rectangle GetQuestLogPanelRect()
    {
        return new Rectangle(10, 92, 600, Math.Max(360, graphics.PreferredBackBufferHeight - 236));
    }

    private static Rectangle GetActTabRect(Rectangle panel, int index)
    {
        return new Rectangle(panel.X + 10 + (index * 86), panel.Y + 56, 82, 24);
    }

    private static Rectangle GetZoneIconRect(Rectangle panel, int index)
    {
        var rects = BuildZoneIconRects(panel, index + 1);
        return index < rects.Count
            ? rects[index]
            : Rectangle.Empty;
    }

    private static List<Rectangle> BuildZoneIconRects(Rectangle panel, int count)
    {
        var result = new List<Rectangle>(Math.Max(0, count));
        if (count <= 0)
        {
            return result;
        }

        var grid = new Rectangle(panel.X + 12, panel.Y + 92, panel.Width - 24, 128);
        const int maxCols = 6;
        const int iconSize = 56;
        const int rowGap = 10;
        var remaining = count;
        var row = 0;

        while (remaining > 0 && row < 2)
        {
            var rowCount = Math.Min(maxCols, remaining);
            var usableWidth = Math.Max(1, grid.Width - (rowCount * iconSize));
            var gap = rowCount > 0 ? usableWidth / (rowCount + 1) : 0;
            var y = grid.Y + 10 + row * (iconSize + rowGap);
            var x = grid.X + gap;

            for (var i = 0; i < rowCount; i++)
            {
                result.Add(new Rectangle(x + i * (iconSize + gap), y, iconSize, iconSize));
            }

            remaining -= rowCount;
            row++;
        }

        return result;
    }

    private static Rectangle GetQuestRowRect(Rectangle panel, int index)
    {
        return new Rectangle(panel.X + 20, panel.Y + 266 + (index * 30), 224, 26);
    }

    private bool TryGetSelectedAct(out QuestLogActState act)
    {
        act = default!;
        if (questLogActs.Count == 0)
        {
            return false;
        }

        selectedQuestActIndex = Math.Clamp(selectedQuestActIndex, 0, questLogActs.Count - 1);
        act = questLogActs[selectedQuestActIndex];
        return true;
    }

    private bool TryGetSelectedZone(out QuestLogZoneState zone)
    {
        zone = default!;
        if (!TryGetSelectedAct(out var act) || act.Zones.Count == 0)
        {
            return false;
        }

        selectedQuestZoneIndex = Math.Clamp(selectedQuestZoneIndex, 0, act.Zones.Count - 1);
        zone = act.Zones[selectedQuestZoneIndex];
        return true;
    }

    private void RebuildQuestLog(IReadOnlyList<WorldObjectiveSnapshot> objectives)
    {
        questLogActs.Clear();
        var acts = new Dictionary<string, QuestLogActState>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < objectives.Count; i++)
        {
            var objective = objectives[i];
            var questId = ParseQuestId(objective.ObjectiveId);
            var actKey = ParseActKey(questId);
            var zoneKey = ParseZoneKey(objective, questId);

            if (!acts.TryGetValue(actKey, out var act))
            {
                act = new QuestLogActState(actKey, FormatActTitle(actKey));
                acts[actKey] = act;
            }

            if (!act.ZoneByKey.TryGetValue(zoneKey, out var zone))
            {
                zone = new QuestLogZoneState(zoneKey, FormatZoneTitle(zoneKey));
                act.ZoneByKey[zoneKey] = zone;
                act.Zones.Add(zone);
            }

            if (!zone.QuestById.TryGetValue(questId, out var quest))
            {
                quest = new QuestLogQuestState(questId, FormatQuestTitle(questId));
                zone.QuestById[questId] = quest;
                zone.Quests.Add(quest);
            }

            quest.Objectives.Add(new QuestLogObjectiveState(
                Label: BuildObjectiveLabel(objective),
                Current: objective.Current,
                Required: Math.Max((ushort)1, objective.Required),
                StateCode: objective.State));
        }

        var orderedActs = acts.Values.OrderBy(x => x.SortKey, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < orderedActs.Count; i++)
        {
            var act = orderedActs[i];
            act.Zones.Sort((a, b) => CompareZoneKeys(a.Key, b.Key));

            for (var z = 0; z < act.Zones.Count; z++)
            {
                var zone = act.Zones[z];
                zone.Quests.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));

                var zoneState = (byte)0;
                for (var q = 0; q < zone.Quests.Count; q++)
                {
                    var quest = zone.Quests[q];
                    quest.TotalObjectives = quest.Objectives.Count;
                    quest.CompletedObjectives = 0;
                    var hasActiveObjective = false;
                    var hasUnlockedObjective = false;
                    for (var k = 0; k < quest.Objectives.Count; k++)
                    {
                        if (quest.Objectives[k].StateCode == 2)
                        {
                            quest.CompletedObjectives++;
                            hasUnlockedObjective = true;
                        }
                        else if (quest.Objectives[k].StateCode == 1)
                        {
                            hasActiveObjective = true;
                            hasUnlockedObjective = true;
                        }
                    }

                    if (quest.TotalObjectives == 0)
                    {
                        quest.StateCode = 0;
                    }
                    else if (quest.CompletedObjectives >= quest.TotalObjectives)
                    {
                        quest.StateCode = 2;
                    }
                    else if (hasActiveObjective || hasUnlockedObjective)
                    {
                        quest.StateCode = 1;
                    }
                    else
                    {
                        quest.StateCode = 0;
                    }

                    if (quest.StateCode == 1)
                    {
                        zoneState = 1;
                    }
                    else if (quest.StateCode == 2 && zoneState == 0)
                    {
                        zoneState = 2;
                    }
                }

                zone.StateCode = zoneState;
            }

            questLogActs.Add(act);
        }

        if (questLogActs.Count == 0)
        {
            selectedQuestActIndex = 0;
            selectedQuestZoneIndex = 0;
            selectedQuestIndex = 0;
            return;
        }

        selectedQuestActIndex = Math.Clamp(selectedQuestActIndex, 0, questLogActs.Count - 1);
        var selectedAct = questLogActs[selectedQuestActIndex];
        selectedQuestZoneIndex = Math.Clamp(selectedQuestZoneIndex, 0, Math.Max(0, selectedAct.Zones.Count - 1));
        var selectedZone = selectedAct.Zones.Count > 0 ? selectedAct.Zones[selectedQuestZoneIndex] : null;
        selectedQuestIndex = selectedZone is null
            ? 0
            : Math.Clamp(selectedQuestIndex, 0, Math.Max(0, selectedZone.Quests.Count - 1));
    }

    private static string ParseQuestId(string objectiveId)
    {
        if (string.IsNullOrWhiteSpace(objectiveId))
        {
            return "quest.unknown";
        }

        var marker = objectiveId.IndexOf(":obj:", StringComparison.Ordinal);
        return marker > 0 ? objectiveId[..marker] : objectiveId;
    }

    private static string ParseActKey(string questId)
    {
        var parts = questId.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("act", StringComparison.OrdinalIgnoreCase))
            {
                return parts[i].ToLowerInvariant();
            }
        }

        return "act?";
    }

    private static string ParseZoneKey(in WorldObjectiveSnapshot objective, string questId)
    {
        if (!string.IsNullOrWhiteSpace(questId) &&
            QuestZoneOverrides.TryGetValue(questId, out var overriddenZone))
        {
            return overriddenZone;
        }

        if (!string.IsNullOrWhiteSpace(objective.EncounterId))
        {
            var parts = objective.EncounterId.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && string.Equals(parts[0], "enc", StringComparison.OrdinalIgnoreCase))
            {
                var key = parts[1].ToLowerInvariant();
                if (key is "cc1" or "cc2" or "cc3")
                {
                    return "cc";
                }

                return key;
            }
        }

        return "hub";
    }

    private static string FormatActTitle(string actKey)
    {
        if (actKey.Length >= 4 && actKey.StartsWith("act", StringComparison.OrdinalIgnoreCase))
        {
            return $"Act {actKey[3..]}";
        }

        return "Act ?";
    }

    private static string FormatZoneTitle(string zoneKey)
    {
        return zoneKey.ToLowerInvariant() switch
        {
            "cp" => "Camp Perimeter",
            "bt" => "Bloody Thicket",
            "gg" => "Graveyard Gate",
            "ig" => "Inner Graveyard",
            "ma" => "Mausoleum",
            "cc" => "Cursed Cathedral",
            "hub" => "Camp",
            _ => Capitalize(zoneKey)
        };
    }

    private static int CompareZoneKeys(string a, string b)
    {
        var ia = Array.IndexOf(ZoneSortOrder, a);
        var ib = Array.IndexOf(ZoneSortOrder, b);
        if (ia >= 0 && ib >= 0)
        {
            return ia.CompareTo(ib);
        }

        if (ia >= 0)
        {
            return -1;
        }

        if (ib >= 0)
        {
            return 1;
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatQuestTitle(string questId)
    {
        var parts = questId.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "Unknown Quest";
        }

        var nameParts = new List<string>();
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Equals("quest", StringComparison.OrdinalIgnoreCase) ||
                parts[i].StartsWith("act", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            nameParts.Add(Capitalize(parts[i]));
        }

        return nameParts.Count == 0
            ? "Quest"
            : string.Join(" ", nameParts).Replace("_", " ", StringComparison.Ordinal);
    }

    private static string BuildObjectiveLabel(WorldObjectiveSnapshot objective)
    {
        var kind = objective.Kind?.Trim();
        var target = objective.TargetId?.Trim();
        if (string.IsNullOrWhiteSpace(kind))
        {
            return string.IsNullOrWhiteSpace(target) ? "Objective" : target!;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return kind!;
        }

        return $"{kind}: {target}";
    }

    private static Color ResolveZoneStateColor(byte stateCode)
    {
        return stateCode switch
        {
            2 => new Color(148, 186, 126),
            1 => new Color(214, 170, 86),
            _ => new Color(110, 116, 132)
        };
    }

    private static Color ResolveQuestStateColor(byte stateCode)
    {
        return stateCode switch
        {
            2 => new Color(154, 206, 136),
            1 => new Color(245, 220, 146),
            _ => new Color(186, 194, 212)
        };
    }

    private static Color ResolveObjectiveStateColor(byte stateCode)
    {
        return stateCode switch
        {
            2 => new Color(154, 206, 136),
            1 => new Color(225, 231, 244),
            _ => new Color(160, 169, 186)
        };
    }

    private sealed class QuestLogActState
    {
        public QuestLogActState(string sortKey, string title)
        {
            SortKey = sortKey;
            Title = title;
        }

        public string SortKey { get; }
        public string Title { get; }
        public List<QuestLogZoneState> Zones { get; } = new();
        public Dictionary<string, QuestLogZoneState> ZoneByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class QuestLogZoneState
    {
        public QuestLogZoneState(string key, string title)
        {
            Key = key;
            Title = title;
        }

        public string Key { get; }
        public string Title { get; }
        public byte StateCode { get; set; }
        public List<QuestLogQuestState> Quests { get; } = new();
        public Dictionary<string, QuestLogQuestState> QuestById { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class QuestLogQuestState
    {
        public QuestLogQuestState(string id, string title)
        {
            Id = id;
            Title = title;
        }

        public string Id { get; }
        public string Title { get; }
        public int CompletedObjectives { get; set; }
        public int TotalObjectives { get; set; }
        public byte StateCode { get; set; }
        public string StateLabel => StateCode switch
        {
            2 => "Completed",
            1 => "Active",
            _ => "Locked"
        };
        public List<QuestLogObjectiveState> Objectives { get; } = new();
    }

    private readonly record struct QuestLogObjectiveState(string Label, ushort Current, ushort Required, byte StateCode);
}
