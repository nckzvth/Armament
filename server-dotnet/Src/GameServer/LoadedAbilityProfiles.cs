using System.Collections.Generic;
using Armament.SharedSim.Sim;

namespace Armament.GameServer;

public sealed class LoadedAbilityProfiles
{
    private readonly Dictionary<string, SimAbilityProfile> _bySpecId;
    private readonly Dictionary<string, SimZoneDefinition> _zoneDefinitions;
    private readonly Dictionary<string, SimLinkDefinition> _linkDefinitions;

    public LoadedAbilityProfiles(
        Dictionary<string, SimAbilityProfile> bySpecId,
        Dictionary<string, SimZoneDefinition> zoneDefinitions,
        Dictionary<string, SimLinkDefinition> linkDefinitions,
        string fallbackSpecId,
        string message)
    {
        _bySpecId = bySpecId;
        _zoneDefinitions = zoneDefinitions;
        _linkDefinitions = linkDefinitions;
        FallbackSpecId = fallbackSpecId;
        Message = message;
    }

    public IReadOnlyDictionary<string, SimAbilityProfile> BySpecId => _bySpecId;
    public IReadOnlyDictionary<string, SimZoneDefinition> ZoneDefinitions => _zoneDefinitions;
    public IReadOnlyDictionary<string, SimLinkDefinition> LinkDefinitions => _linkDefinitions;
    public string FallbackSpecId { get; }
    public string Message { get; }

    public SimAbilityProfile ResolveForSpec(string? specId)
    {
        if (!string.IsNullOrWhiteSpace(specId) && _bySpecId.TryGetValue(specId, out var profile))
        {
            return profile;
        }

        if (_bySpecId.TryGetValue(FallbackSpecId, out profile))
        {
            return profile;
        }

        return SimAbilityProfiles.BuiltinV1;
    }
}
