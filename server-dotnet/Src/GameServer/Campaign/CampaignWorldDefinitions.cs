#nullable enable
using System;
using System.Collections.Generic;

namespace Armament.GameServer.Campaign;

public sealed class CampaignWorldDefinition
{
    public string StartZoneId { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, CampaignZoneDefinition> Zones { get; set; } = new Dictionary<string, CampaignZoneDefinition>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, CampaignEncounterDefinition> Encounters { get; set; } = new Dictionary<string, CampaignEncounterDefinition>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, CampaignQuestDefinition> Quests { get; set; } = new Dictionary<string, CampaignQuestDefinition>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, CampaignNpcDefinition> Npcs { get; set; } = new Dictionary<string, CampaignNpcDefinition>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, CampaignObjectDefinition> Objects { get; set; } = new Dictionary<string, CampaignObjectDefinition>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, CampaignHazardDefinition> Hazards { get; set; } = new Dictionary<string, CampaignHazardDefinition>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, CampaignZoneLayoutDefinition> ZoneLayouts { get; set; } = new Dictionary<string, CampaignZoneLayoutDefinition>(StringComparer.Ordinal);
}

public sealed class CampaignZoneDefinition
{
    public string Id { get; set; } = string.Empty;
    public List<string> EncounterIds { get; set; } = new();
}

public sealed class CampaignObjectDefinition
{
    public string Id { get; set; } = string.Empty;
    public int MaxHealth { get; set; }
    public string InteractMode { get; set; } = string.Empty;
    public List<string> Flags { get; set; } = new();
    public string? LinkedHazardId { get; set; }
}

public sealed class CampaignNpcDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public int InteractRadiusMilli { get; set; } = 2000;
}

public sealed class CampaignHazardDefinition
{
    public string Id { get; set; } = string.Empty;
    public int TickIntervalTicks { get; set; }
    public int DurationTicks { get; set; }
    public List<string> EffectTags { get; set; } = new();
}

public sealed class CampaignZoneLayoutDefinition
{
    public string ZoneId { get; set; } = string.Empty;
    public Dictionary<string, CampaignEncounterLayoutDefinition> Encounters { get; set; } = new(StringComparer.Ordinal);
    public List<CampaignNpcPlacementDefinition> NpcPlacements { get; set; } = new();
}

public sealed class CampaignEncounterLayoutDefinition
{
    public string EncounterId { get; set; } = string.Empty;
    public int? AnchorXMilli { get; set; }
    public int? AnchorYMilli { get; set; }
    public List<CampaignObjectPlacementDefinition> ObjectPlacements { get; set; } = new();
    public List<CampaignHazardPlacementDefinition> HazardPlacements { get; set; } = new();
}

public sealed class CampaignNpcPlacementDefinition
{
    public string NpcId { get; set; } = string.Empty;
    public int XMilli { get; set; }
    public int YMilli { get; set; }
}

public sealed class CampaignObjectPlacementDefinition
{
    public string ObjectDefId { get; set; } = string.Empty;
    public int XMilli { get; set; }
    public int YMilli { get; set; }
}

public sealed class CampaignHazardPlacementDefinition
{
    public string HazardId { get; set; } = string.Empty;
    public int XMilli { get; set; }
    public int YMilli { get; set; }
}
