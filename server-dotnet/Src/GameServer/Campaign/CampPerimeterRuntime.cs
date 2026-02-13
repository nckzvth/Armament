#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Armament.SharedSim.Sim;

namespace Armament.GameServer.Campaign;

public enum CampaignCompletionEventKind : byte
{
    EnemyKilled = 1,
    TokenCollected = 2,
    ObjectDestroyed = 3,
    ObjectiveCompleted = 4,
    PlayerEnteredRegion = 5
}

public enum CampaignEncounterStateKind : byte
{
    Inactive = 0,
    Active = 1,
    Completed = 2,
    Resetting = 3
}

public enum CampaignQuestStateKind : byte
{
    Inactive = 0,
    Active = 1,
    Completed = 2
}

public enum CampaignObjectiveStateKind : byte
{
    Inactive = 0,
    Active = 1,
    Completed = 2
}

public sealed class CampaignEncounterDefinition
{
    public string Id { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public List<string> EnemyIds { get; set; } = new();
    public CampaignCompletionEventKind CompletionEventKind { get; set; } = CampaignCompletionEventKind.EnemyKilled;
    public int CompletionCount { get; set; } = 1;
    public string? RewardItemCode { get; set; }
    public int RewardItemQuantity { get; set; } = 1;
    public int ResetTicks { get; set; } = 900;
    public List<string> ObjectiveObjectIds { get; set; } = new();
    public List<string> HazardIds { get; set; } = new();
}

public sealed class CampaignQuestObjectiveDefinition
{
    public string Type { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}

public sealed class CampaignQuestDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> PrerequisiteQuestIds { get; set; } = new();
    public List<CampaignQuestObjectiveDefinition> Objectives { get; set; } = new();
}

public readonly struct CampaignEncounterCompletion
{
    public CampaignEncounterCompletion(Guid characterId, string encounterId, string? rewardItemCode, int rewardItemQuantity)
    {
        CharacterId = characterId;
        EncounterId = encounterId;
        RewardItemCode = rewardItemCode;
        RewardItemQuantity = rewardItemQuantity;
    }

    public Guid CharacterId { get; }
    public string EncounterId { get; }
    public string? RewardItemCode { get; }
    public int RewardItemQuantity { get; }
}

public readonly struct CampaignObjectiveSnapshot
{
    public CampaignObjectiveSnapshot(
        string objectiveId,
        string encounterId,
        string kind,
        string targetId,
        int current,
        int required,
        CampaignObjectiveStateKind state)
    {
        ObjectiveId = objectiveId;
        EncounterId = encounterId;
        Kind = kind;
        TargetId = targetId;
        Current = current;
        Required = required;
        State = state;
    }

    public string ObjectiveId { get; }
    public string EncounterId { get; }
    public string Kind { get; }
    public string TargetId { get; }
    public int Current { get; }
    public int Required { get; }
    public CampaignObjectiveStateKind State { get; }
}

public sealed class CampPerimeterRuntime
{
    private readonly Dictionary<string, CampaignEncounterDefinition> _encounters;
    private readonly Dictionary<string, CampaignQuestDefinition> _quests;
    private readonly Dictionary<string, List<(string QuestId, int ObjectiveIndex)>> _encounterObjectives = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _objectToEncounterIds = new(StringComparer.Ordinal);
    private readonly List<CampaignEncounterCompletion> _completionBuffer = new();
    private readonly Dictionary<Guid, CharacterCampaignState> _stateByCharacter = new();
    private readonly HashSet<Guid> _dirtyCharacters = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public CampPerimeterRuntime(
        IReadOnlyDictionary<string, CampaignEncounterDefinition> encounters,
        IReadOnlyDictionary<string, CampaignQuestDefinition>? quests = null)
    {
        _encounters = new Dictionary<string, CampaignEncounterDefinition>(encounters, StringComparer.Ordinal);
        _quests = quests is null
            ? new Dictionary<string, CampaignQuestDefinition>(StringComparer.Ordinal)
            : new Dictionary<string, CampaignQuestDefinition>(quests, StringComparer.Ordinal);

        foreach (var quest in _quests.Values)
        {
            for (var i = 0; i < quest.Objectives.Count; i++)
            {
                var objective = quest.Objectives[i];
                if (!objective.Type.Equals("CompleteEncounter", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!_encounterObjectives.TryGetValue(objective.TargetId, out var refs))
                {
                    refs = new List<(string QuestId, int ObjectiveIndex)>();
                    _encounterObjectives[objective.TargetId] = refs;
                }

                refs.Add((quest.Id, i));
            }
        }

        foreach (var encounter in _encounters.Values)
        {
            for (var i = 0; i < encounter.ObjectiveObjectIds.Count; i++)
            {
                var objectDefId = encounter.ObjectiveObjectIds[i];
                if (!_objectToEncounterIds.TryGetValue(objectDefId, out var encounterIds))
                {
                    encounterIds = new List<string>();
                    _objectToEncounterIds[objectDefId] = encounterIds;
                }

                if (!encounterIds.Contains(encounter.Id, StringComparer.Ordinal))
                {
                    encounterIds.Add(encounter.Id);
                }
            }
        }
    }

    public IReadOnlyList<CampaignEncounterCompletion> Consume(
        IReadOnlyList<SimEventRecord> events,
        Func<uint, Guid?> resolveCharacterIdByPlayerEntity,
        Func<uint, string?>? resolveObjectDefIdByRuntimeId = null,
        Func<uint, string?>? resolveNpcIdByRuntimeId = null)
    {
        _completionBuffer.Clear();
        _dirtyCharacters.Clear();

        var nowTick = events.Count > 0 ? events[^1].Tick : 0u;

        foreach (var (characterId, state) in _stateByCharacter)
        {
            var changed = TickCharacterStateMachine(characterId, state, nowTick);
            if (changed)
            {
                _dirtyCharacters.Add(characterId);
            }
        }

        for (var i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            if (evt.PlayerEntityId == 0)
            {
                continue;
            }

            var characterId = resolveCharacterIdByPlayerEntity(evt.PlayerEntityId);
            if (characterId is null || characterId == Guid.Empty)
            {
                continue;
            }

            var character = GetCharacterState(characterId.Value);
            var changed = ProcessEvent(characterId.Value, character, evt, resolveObjectDefIdByRuntimeId, resolveNpcIdByRuntimeId);
            if (!changed)
            {
                continue;
            }

            _dirtyCharacters.Add(characterId.Value);
            CompleteEligibleQuests(character);
            ActivateAvailableQuests(character);
        }

        return _completionBuffer;
    }

    public void RestoreCharacterState(Guid characterId, string? progressJson)
    {
        var state = GetCharacterState(characterId);

        state.ActiveQuests.Clear();
        state.CompletedQuests.Clear();
        state.CompletedRewardEncounters.Clear();
        state.ObjectiveProgress.Clear();
        state.EncounterStates.Clear();

        if (string.IsNullOrWhiteSpace(progressJson) || progressJson == "{}")
        {
            ActivateAvailableQuests(state);
            _dirtyCharacters.Add(characterId);
            return;
        }

        CampaignProgressPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CampaignProgressPayload>(progressJson, JsonOptions);
        }
        catch
        {
            payload = null;
        }

        if (payload is null)
        {
            ActivateAvailableQuests(state);
            _dirtyCharacters.Add(characterId);
            return;
        }

        if (payload.ActiveQuestIds is not null)
        {
            for (var i = 0; i < payload.ActiveQuestIds.Count; i++)
            {
                var questId = payload.ActiveQuestIds[i];
                if (_quests.ContainsKey(questId))
                {
                    state.ActiveQuests.Add(questId);
                }
            }
        }

        if (payload.CompletedQuestIds is not null)
        {
            for (var i = 0; i < payload.CompletedQuestIds.Count; i++)
            {
                var questId = payload.CompletedQuestIds[i];
                if (_quests.ContainsKey(questId))
                {
                    state.CompletedQuests.Add(questId);
                }
            }
        }

        if (payload.ObjectiveProgress is not null)
        {
            foreach (var kvp in payload.ObjectiveProgress)
            {
                state.ObjectiveProgress[kvp.Key] = Math.Max(0, kvp.Value);
            }
        }

        if (payload.CompletedRewardEncounterIds is not null)
        {
            for (var i = 0; i < payload.CompletedRewardEncounterIds.Count; i++)
            {
                var encounterId = payload.CompletedRewardEncounterIds[i];
                if (_encounters.ContainsKey(encounterId))
                {
                    state.CompletedRewardEncounters.Add(encounterId);
                }
            }
        }

        if (payload.EncounterStates is not null)
        {
            foreach (var kvp in payload.EncounterStates)
            {
                if (!_encounters.ContainsKey(kvp.Key))
                {
                    continue;
                }

                state.EncounterStates[kvp.Key] = new EncounterRuntimeState
                {
                    ProgressCount = Math.Max(0, kvp.Value.ProgressCount),
                    ResetRemainingTicks = Math.Max(0, kvp.Value.ResetRemainingTicks),
                    State = kvp.Value.State
                };
            }
        }

        ActivateAvailableQuests(state);
    }

    public string ExportCharacterState(Guid characterId)
    {
        if (!_stateByCharacter.TryGetValue(characterId, out var state))
        {
            return "{}";
        }

        var payload = new CampaignProgressPayload
        {
            ActiveQuestIds = state.ActiveQuests.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            CompletedQuestIds = state.CompletedQuests.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            CompletedRewardEncounterIds = state.CompletedRewardEncounters.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            ObjectiveProgress = new Dictionary<string, int>(state.ObjectiveProgress, StringComparer.Ordinal),
            EncounterStates = state.EncounterStates.ToDictionary(
                kvp => kvp.Key,
                kvp => new EncounterStatePayload
                {
                    State = kvp.Value.State,
                    ProgressCount = kvp.Value.ProgressCount,
                    ResetRemainingTicks = kvp.Value.ResetRemainingTicks
                },
                StringComparer.Ordinal)
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public IReadOnlyCollection<Guid> ConsumeDirtyCharacterIds()
    {
        if (_dirtyCharacters.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var values = _dirtyCharacters.ToArray();
        _dirtyCharacters.Clear();
        return values;
    }

    public IReadOnlyCollection<string> BuildActiveEncounterIds(IReadOnlyCollection<Guid> onlineCharacterIds)
    {
        if (onlineCharacterIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var characterId in onlineCharacterIds)
        {
            var state = GetCharacterState(characterId);
            var activeForCharacter = ComputeActiveEncounterIds(state);
            foreach (var id in activeForCharacter)
            {
                ids.Add(id);
            }
        }

        return ids.ToArray();
    }

    public IReadOnlyList<CampaignObjectiveSnapshot> BuildObjectiveSnapshots(Guid characterId)
    {
        var snapshots = new List<CampaignObjectiveSnapshot>();
        var character = GetCharacterState(characterId);

        foreach (var quest in _quests.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var questState = ResolveQuestState(character, quest);
            for (var i = 0; i < quest.Objectives.Count; i++)
            {
                var objective = quest.Objectives[i];
                var objectiveId = BuildQuestObjectiveKey(quest.Id, i);
                var current = GetObjectiveProgress(character, objectiveId);
                var required = Math.Max(1, objective.Count);
                var state = questState switch
                {
                    CampaignQuestStateKind.Inactive => CampaignObjectiveStateKind.Inactive,
                    CampaignQuestStateKind.Completed => CampaignObjectiveStateKind.Completed,
                    _ => current >= required ? CampaignObjectiveStateKind.Completed : CampaignObjectiveStateKind.Active
                };

                snapshots.Add(new CampaignObjectiveSnapshot(
                    objectiveId,
                    ResolveObjectiveEncounterId(character, objective),
                    objective.Type,
                    objective.TargetId,
                    current,
                    required,
                    state));
            }
        }

        return snapshots;
    }

    public CampaignEncounterStateKind ResolveEncounterState(Guid characterId, string encounterId)
    {
        if (!_encounters.ContainsKey(encounterId) || !_stateByCharacter.TryGetValue(characterId, out var character))
        {
            return CampaignEncounterStateKind.Inactive;
        }

        if (!character.EncounterStates.TryGetValue(encounterId, out var state))
        {
            return CampaignEncounterStateKind.Inactive;
        }

        return state.State;
    }

    private CharacterCampaignState GetCharacterState(Guid characterId)
    {
        if (!_stateByCharacter.TryGetValue(characterId, out var state))
        {
            state = new CharacterCampaignState();
            _stateByCharacter[characterId] = state;
            ActivateAvailableQuests(state);
        }

        return state;
    }

    private bool TickCharacterStateMachine(Guid characterId, CharacterCampaignState character, uint nowTick)
    {
        var changed = false;

        ActivateAvailableQuests(character);
        var activeEncounterIds = ComputeActiveEncounterIds(character);

        foreach (var encounter in _encounters.Values)
        {
            var state = GetEncounterState(character, encounter.Id);
            var shouldBeActive = activeEncounterIds.Contains(encounter.Id);

            if (state.State == CampaignEncounterStateKind.Resetting)
            {
                if (state.ResetRemainingTicks > 0)
                {
                    state.ResetRemainingTicks--;
                }

                if (state.ResetRemainingTicks <= 0)
                {
                    state.State = shouldBeActive ? CampaignEncounterStateKind.Active : CampaignEncounterStateKind.Inactive;
                    state.ProgressCount = 0;
                    changed = true;
                }

                continue;
            }

            if (state.State == CampaignEncounterStateKind.Completed)
            {
                if (encounter.ResetTicks > 0)
                {
                    state.State = CampaignEncounterStateKind.Resetting;
                    state.ResetRemainingTicks = encounter.ResetTicks;
                    changed = true;
                }

                continue;
            }

            if (shouldBeActive)
            {
                if (state.State == CampaignEncounterStateKind.Inactive)
                {
                    state.State = CampaignEncounterStateKind.Active;
                    state.LastTransitionTick = nowTick;
                    changed = true;
                }
            }
            else if (state.State == CampaignEncounterStateKind.Active)
            {
                state.State = CampaignEncounterStateKind.Inactive;
                state.LastTransitionTick = nowTick;
                changed = true;
            }
        }

        if (changed)
        {
            _dirtyCharacters.Add(characterId);
        }

        return changed;
    }

    private bool ProcessEvent(
        Guid characterId,
        CharacterCampaignState character,
        in SimEventRecord evt,
        Func<uint, string?>? resolveObjectDefIdByRuntimeId,
        Func<uint, string?>? resolveNpcIdByRuntimeId)
    {
        var changed = false;
        var activeEncounterIds = ComputeActiveEncounterIds(character);

        foreach (var encounterId in activeEncounterIds)
        {
            if (!_encounters.TryGetValue(encounterId, out var encounter))
            {
                continue;
            }

            var encounterState = GetEncounterState(character, encounterId);
            if (_quests.Count == 0 && encounterState.State == CampaignEncounterStateKind.Inactive)
            {
                // Backward-compatible behavior: questless runtimes treat encounters as active.
                encounterState.State = CampaignEncounterStateKind.Active;
            }

            if (encounterState.State != CampaignEncounterStateKind.Active)
            {
                continue;
            }

            if (!Matches(evt, encounter.CompletionEventKind))
            {
                continue;
            }

            encounterState.ProgressCount += 1;
            changed = true;

            if (encounterState.ProgressCount < Math.Max(1, encounter.CompletionCount))
            {
                continue;
            }

            encounterState.State = CampaignEncounterStateKind.Completed;
            encounterState.LastTransitionTick = evt.Tick;

            if (!character.CompletedRewardEncounters.Contains(encounter.Id))
            {
                character.CompletedRewardEncounters.Add(encounter.Id);
                _completionBuffer.Add(new CampaignEncounterCompletion(
                    characterId,
                    encounter.Id,
                    encounter.RewardItemCode,
                    Math.Max(1, encounter.RewardItemQuantity)));
            }

            ApplyEncounterCompletionToQuestObjectives(character, encounter.Id);
        }

        changed |= ApplyEventToQuestObjectives(_quests, character, evt, resolveObjectDefIdByRuntimeId, resolveNpcIdByRuntimeId);
        return changed;
    }

    private void ActivateAvailableQuests(CharacterCampaignState character)
    {
        foreach (var quest in _quests.Values)
        {
            var current = ResolveQuestState(character, quest);
            if (current != CampaignQuestStateKind.Inactive)
            {
                continue;
            }

            var canActivate = true;
            for (var i = 0; i < quest.PrerequisiteQuestIds.Count; i++)
            {
                if (!character.CompletedQuests.Contains(quest.PrerequisiteQuestIds[i]))
                {
                    canActivate = false;
                    break;
                }
            }

            if (!canActivate)
            {
                continue;
            }

            character.ActiveQuests.Add(quest.Id);
        }
    }

    private void CompleteEligibleQuests(CharacterCampaignState character)
    {
        foreach (var quest in _quests.Values)
        {
            if (!character.ActiveQuests.Contains(quest.Id) || character.CompletedQuests.Contains(quest.Id))
            {
                continue;
            }

            var complete = true;
            for (var i = 0; i < quest.Objectives.Count; i++)
            {
                var objective = quest.Objectives[i];
                var key = BuildQuestObjectiveKey(quest.Id, i);
                if (GetObjectiveProgress(character, key) < Math.Max(1, objective.Count))
                {
                    complete = false;
                    break;
                }
            }

            if (!complete)
            {
                continue;
            }

            character.ActiveQuests.Remove(quest.Id);
            character.CompletedQuests.Add(quest.Id);
        }
    }

    private HashSet<string> ComputeActiveEncounterIds(CharacterCampaignState character)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        if (_quests.Count == 0)
        {
            foreach (var encounter in _encounters.Keys)
            {
                active.Add(encounter);
            }

            return active;
        }

        foreach (var questId in character.ActiveQuests)
        {
            if (!_quests.TryGetValue(questId, out var quest))
            {
                continue;
            }

            for (var i = 0; i < quest.Objectives.Count; i++)
            {
                var objective = quest.Objectives[i];
                if (objective.Type.Equals("CompleteEncounter", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(objective.TargetId) && _encounters.ContainsKey(objective.TargetId))
                    {
                        active.Add(objective.TargetId);
                    }
                }
                else if (objective.Type.Equals("DestroyObjectType", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(objective.TargetId) &&
                         _objectToEncounterIds.TryGetValue(objective.TargetId, out var encounterIds))
                {
                    for (var j = 0; j < encounterIds.Count; j++)
                    {
                        active.Add(encounterIds[j]);
                    }
                }
            }
        }

        return active;
    }

    private void ApplyEncounterCompletionToQuestObjectives(CharacterCampaignState character, string encounterId)
    {
        if (!_encounterObjectives.TryGetValue(encounterId, out var refs))
        {
            return;
        }

        for (var i = 0; i < refs.Count; i++)
        {
            var (questId, objectiveIndex) = refs[i];
            if (!character.ActiveQuests.Contains(questId))
            {
                continue;
            }

            var key = BuildQuestObjectiveKey(questId, objectiveIndex);
            var value = GetObjectiveProgress(character, key) + 1;
            SetObjectiveProgress(character, key, value);
        }
    }

    private static bool ApplyEventToQuestObjectives(
        IReadOnlyDictionary<string, CampaignQuestDefinition> quests,
        CharacterCampaignState character,
        in SimEventRecord evt,
        Func<uint, string?>? resolveObjectDefIdByRuntimeId,
        Func<uint, string?>? resolveNpcIdByRuntimeId)
    {
        var changed = false;

        foreach (var quest in character.ActiveQuests)
        {
            if (!quests.TryGetValue(quest, out var questDef))
            {
                continue;
            }

            for (var i = 0; i < questDef.Objectives.Count; i++)
            {
                var objective = questDef.Objectives[i];
                var key = BuildQuestObjectiveKey(quest, i);

                if (objective.Type.Equals("DestroyObjectType", StringComparison.OrdinalIgnoreCase) &&
                    evt.Kind == SimEventKind.ObjectDestroyed &&
                    evt.SubjectObjectId != 0 &&
                    resolveObjectDefIdByRuntimeId is not null &&
                    string.Equals(resolveObjectDefIdByRuntimeId(evt.SubjectObjectId), objective.TargetId, StringComparison.Ordinal))
                {
                    character.ObjectiveProgress[key] = GetObjectiveProgress(character, key) + 1;
                    changed = true;
                }
                else if (objective.Type.Equals("TalkToNpc", StringComparison.OrdinalIgnoreCase) &&
                         evt.Kind == SimEventKind.NpcInteracted &&
                         evt.SubjectEntityId != 0 &&
                         resolveNpcIdByRuntimeId is not null &&
                         string.Equals(resolveNpcIdByRuntimeId(evt.SubjectEntityId), objective.TargetId, StringComparison.Ordinal))
                {
                    character.ObjectiveProgress[key] = GetObjectiveProgress(character, key) + 1;
                    changed = true;
                }
                else if (objective.Type.Equals("CollectToken", StringComparison.OrdinalIgnoreCase) && evt.Kind == SimEventKind.TokenCollected)
                {
                    character.ObjectiveProgress[key] = GetObjectiveProgress(character, key) + 1;
                    changed = true;
                }
                else if (objective.Type.Equals("EnterRegion", StringComparison.OrdinalIgnoreCase) && evt.Kind == SimEventKind.PlayerEnteredRegion)
                {
                    character.ObjectiveProgress[key] = GetObjectiveProgress(character, key) + 1;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool Matches(in SimEventRecord evt, CampaignCompletionEventKind kind)
    {
        return kind switch
        {
            CampaignCompletionEventKind.EnemyKilled => evt.Kind == SimEventKind.EnemyKilled,
            CampaignCompletionEventKind.TokenCollected => evt.Kind == SimEventKind.TokenCollected,
            CampaignCompletionEventKind.ObjectDestroyed => evt.Kind == SimEventKind.ObjectDestroyed,
            CampaignCompletionEventKind.ObjectiveCompleted => evt.Kind == SimEventKind.ObjectiveCompleted,
            CampaignCompletionEventKind.PlayerEnteredRegion => evt.Kind == SimEventKind.PlayerEnteredRegion,
            _ => false
        };
    }

    private static CampaignQuestStateKind ResolveQuestState(CharacterCampaignState character, CampaignQuestDefinition quest)
    {
        if (character.CompletedQuests.Contains(quest.Id))
        {
            return CampaignQuestStateKind.Completed;
        }

        return character.ActiveQuests.Contains(quest.Id)
            ? CampaignQuestStateKind.Active
            : CampaignQuestStateKind.Inactive;
    }

    private static int GetObjectiveProgress(CharacterCampaignState character, string key)
    {
        return character.ObjectiveProgress.TryGetValue(key, out var value)
            ? value
            : 0;
    }

    private static void SetObjectiveProgress(CharacterCampaignState character, string key, int value)
    {
        character.ObjectiveProgress[key] = Math.Max(0, value);
    }

    private static string BuildQuestObjectiveKey(string questId, int objectiveIndex) => $"{questId}:obj:{objectiveIndex}";

    private string ResolveObjectiveEncounterId(CharacterCampaignState character, CampaignQuestObjectiveDefinition objective)
    {
        if (objective.Type.Equals("CompleteEncounter", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(objective.TargetId))
        {
            return objective.TargetId;
        }

        if (objective.Type.Equals("DestroyObjectType", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(objective.TargetId) &&
            _objectToEncounterIds.TryGetValue(objective.TargetId, out var encounterIds) &&
            encounterIds.Count > 0)
        {
            // Choose first deterministic encounter id for UI marker purposes.
            return encounterIds.OrderBy(x => x, StringComparer.Ordinal).First();
        }

        return string.Empty;
    }

    private EncounterRuntimeState GetEncounterState(CharacterCampaignState character, string encounterId)
    {
        if (!character.EncounterStates.TryGetValue(encounterId, out var state))
        {
            state = new EncounterRuntimeState();
            character.EncounterStates[encounterId] = state;
        }

        return state;
    }

    private sealed class CharacterCampaignState
    {
        public HashSet<string> ActiveQuests { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CompletedQuests { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CompletedRewardEncounters { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> ObjectiveProgress { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, EncounterRuntimeState> EncounterStates { get; } = new(StringComparer.Ordinal);
    }

    private sealed class EncounterRuntimeState
    {
        public CampaignEncounterStateKind State { get; set; }
        public int ProgressCount { get; set; }
        public int ResetRemainingTicks { get; set; }
        public uint LastTransitionTick { get; set; }
    }

    private sealed class CampaignProgressPayload
    {
        public List<string>? ActiveQuestIds { get; set; }
        public List<string>? CompletedQuestIds { get; set; }
        public List<string>? CompletedRewardEncounterIds { get; set; }
        public Dictionary<string, int>? ObjectiveProgress { get; set; }
        public Dictionary<string, EncounterStatePayload>? EncounterStates { get; set; }
    }

    private sealed class EncounterStatePayload
    {
        public CampaignEncounterStateKind State { get; set; }
        public int ProgressCount { get; set; }
        public int ResetRemainingTicks { get; set; }
    }
}
