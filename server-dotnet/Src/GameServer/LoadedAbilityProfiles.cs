using System.Collections.Generic;
using Armament.SharedSim.Sim;

namespace Armament.GameServer;

public sealed class LoadedAbilityProfiles
{
    private readonly Dictionary<string, SimAbilityProfile> _bySpecId;

    public LoadedAbilityProfiles(Dictionary<string, SimAbilityProfile> bySpecId, string fallbackSpecId, string message)
    {
        _bySpecId = bySpecId;
        FallbackSpecId = fallbackSpecId;
        Message = message;
    }

    public IReadOnlyDictionary<string, SimAbilityProfile> BySpecId => _bySpecId;
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
