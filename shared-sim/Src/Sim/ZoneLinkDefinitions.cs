#nullable enable
using System.Collections.Generic;

namespace Armament.SharedSim.Sim;

public sealed class SimZoneDefinition
{
    public string Id { get; set; } = string.Empty;
    public int RadiusMilli { get; set; }
    public int DurationTicks { get; set; }
    public int TickIntervalTicks { get; set; }
    public int DamagePerPulse { get; set; }
    public int HealPerPulse { get; set; }
    public string? StatusId { get; set; }
    public int StatusDurationTicks { get; set; }
}

public sealed class SimLinkDefinition
{
    public string Id { get; set; } = string.Empty;
    public int DurationTicks { get; set; }
    public int MaxDistanceMilli { get; set; }
    public int PullMilliPerTick { get; set; }
    public int DamagePerTick { get; set; }
    public int MaxActiveLinks { get; set; }
}

public static class SimZoneLinkDefaults
{
    public static IEnumerable<SimZoneDefinition> Zones
    {
        get
        {
            yield return new SimZoneDefinition
            {
                Id = "zone.exorcist.warden.abjuration_field",
                RadiusMilli = 1900,
                DurationTicks = 300,
                TickIntervalTicks = 18,
                DamagePerPulse = 4,
                HealPerPulse = 0,
                StatusId = "status.exorcist.warden.bound",
                StatusDurationTicks = 180
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.exorcist.inquisitor.abjuration_field",
                RadiusMilli = 1900,
                DurationTicks = 300,
                TickIntervalTicks = 18,
                DamagePerPulse = 5,
                HealPerPulse = 0,
                StatusId = "status.exorcist.inquisitor.bound",
                StatusDurationTicks = 180
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.bastion.bulwark.fissure",
                RadiusMilli = 1600,
                DurationTicks = 260,
                TickIntervalTicks = 20,
                DamagePerPulse = 3,
                HealPerPulse = 0,
                StatusDurationTicks = 1
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.bastion.cataclysm.fissure",
                RadiusMilli = 1700,
                DurationTicks = 260,
                TickIntervalTicks = 18,
                DamagePerPulse = 4,
                HealPerPulse = 0,
                StatusId = "status.bastion.cataclysm.scorched",
                StatusDurationTicks = 180
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.bastion.bulwark.caldera",
                RadiusMilli = 2200,
                DurationTicks = 320,
                TickIntervalTicks = 14,
                DamagePerPulse = 6,
                HealPerPulse = 0,
                StatusDurationTicks = 1
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.bastion.cataclysm.caldera",
                RadiusMilli = 2300,
                DurationTicks = 320,
                TickIntervalTicks = 12,
                DamagePerPulse = 7,
                HealPerPulse = 0,
                StatusId = "status.bastion.cataclysm.scorched",
                StatusDurationTicks = 180
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.tidebinder.tidecaller.soothing_pool",
                RadiusMilli = 2100,
                DurationTicks = 320,
                TickIntervalTicks = 16,
                DamagePerPulse = 0,
                HealPerPulse = 3,
                StatusId = "status.tidebinder.tidecaller.soaked",
                StatusDurationTicks = 180
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.tidebinder.tidecaller.maelstrom",
                RadiusMilli = 2400,
                DurationTicks = 360,
                TickIntervalTicks = 12,
                DamagePerPulse = 2,
                HealPerPulse = 2,
                StatusId = "status.tidebinder.tidecaller.soaked",
                StatusDurationTicks = 180
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.tidebinder.tempest.vortex_pool",
                RadiusMilli = 2100,
                DurationTicks = 320,
                TickIntervalTicks = 16,
                DamagePerPulse = 4,
                HealPerPulse = 0,
                StatusId = "status.tidebinder.tempest.soaked",
                StatusDurationTicks = 180
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.tidebinder.tempest.maelstrom",
                RadiusMilli = 2400,
                DurationTicks = 360,
                TickIntervalTicks = 12,
                DamagePerPulse = 5,
                HealPerPulse = 0,
                StatusId = "status.tidebinder.tempest.soaked",
                StatusDurationTicks = 180
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.arbiter.aegis.seal",
                RadiusMilli = 2100,
                DurationTicks = 340,
                TickIntervalTicks = 16,
                DamagePerPulse = 0,
                HealPerPulse = 3,
                StatusDurationTicks = 1
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.arbiter.aegis.ward",
                RadiusMilli = 2200,
                DurationTicks = 300,
                TickIntervalTicks = 14,
                DamagePerPulse = 0,
                HealPerPulse = 2,
                StatusDurationTicks = 1
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.arbiter.aegis.decree",
                RadiusMilli = 2400,
                DurationTicks = 360,
                TickIntervalTicks = 12,
                DamagePerPulse = 1,
                HealPerPulse = 4,
                StatusDurationTicks = 1
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.arbiter.edict.seal",
                RadiusMilli = 2100,
                DurationTicks = 320,
                TickIntervalTicks = 16,
                DamagePerPulse = 3,
                HealPerPulse = 0,
                StatusId = "status.arbiter.edict.decreed",
                StatusDurationTicks = 180
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.arbiter.edict.lattice",
                RadiusMilli = 2200,
                DurationTicks = 300,
                TickIntervalTicks = 14,
                DamagePerPulse = 2,
                HealPerPulse = 0,
                StatusId = "status.arbiter.edict.decreed",
                StatusDurationTicks = 180
            };
            yield return new SimZoneDefinition
            {
                Id = "zone.arbiter.edict.decree",
                RadiusMilli = 2400,
                DurationTicks = 360,
                TickIntervalTicks = 12,
                DamagePerPulse = 5,
                HealPerPulse = 0,
                StatusId = "status.arbiter.edict.decreed",
                StatusDurationTicks = 180
            };
        }
    }

    public static IEnumerable<SimLinkDefinition> Links
    {
        get
        {
            yield return new SimLinkDefinition
            {
                Id = "link.dreadweaver.menace.chain_snare",
                DurationTicks = 300,
                MaxDistanceMilli = 6000,
                PullMilliPerTick = 80,
                DamagePerTick = 2,
                MaxActiveLinks = 2
            };
            yield return new SimLinkDefinition
            {
                Id = "link.dreadweaver.deceiver.chain_snare",
                DurationTicks = 260,
                MaxDistanceMilli = 6200,
                PullMilliPerTick = 60,
                DamagePerTick = 3,
                MaxActiveLinks = 1
            };
            yield return new SimLinkDefinition
            {
                Id = "link.arbiter.aegis.constellation_link",
                DurationTicks = 300,
                MaxDistanceMilli = 7000,
                PullMilliPerTick = 0,
                DamagePerTick = 0,
                MaxActiveLinks = 2
            };
            yield return new SimLinkDefinition
            {
                Id = "link.arbiter.edict.constellation_link",
                DurationTicks = 280,
                MaxDistanceMilli = 7000,
                PullMilliPerTick = 0,
                DamagePerTick = 2,
                MaxActiveLinks = 3
            };
        }
    }
}
