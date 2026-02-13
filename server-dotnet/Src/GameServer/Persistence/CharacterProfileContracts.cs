using Armament.SharedSim.Sim;

namespace Armament.GameServer.Persistence;

public sealed record CharacterProfileLoadRequest(
    string EndpointKey,
    uint ClientId,
    string AccountSubject,
    string AccountDisplayName,
    int CharacterSlot,
    string PreferredCharacterName,
    string RequestedBaseClassId,
    string RequestedSpecId);

public sealed record CharacterProfileSaveRequest(
    Guid CharacterId,
    string CharacterName,
    CharacterProfileData Profile);

public sealed record CharacterProfileData(
    int Level,
    uint Experience,
    int Currency,
    CharacterAttributes Attributes,
    string BaseClassId,
    string SpecId,
    string InventoryJson,
    string QuestProgressJson = "{}");

public sealed record CharacterProfileLoadResult(
    string EndpointKey,
    Guid CharacterId,
    string CharacterName,
    CharacterProfileData? Profile);

public interface ICharacterProfileService
{
    bool TryEnqueueLoad(CharacterProfileLoadRequest request);
    bool TryEnqueueSave(CharacterProfileSaveRequest request);
    bool TryDequeueLoaded(out CharacterProfileLoadResult result);
}
