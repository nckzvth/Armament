namespace Armament.Persistence.Abstractions;

public sealed record CharacterAttributesRecord(int Might, int Will, int Alacrity, int Constitution);

public sealed record CharacterContextRecord(
    Guid CharacterId,
    string Name,
    int Level,
    long Experience,
    CharacterAttributesRecord Attributes,
    int MaxHealth,
    int MaxBuilderResource,
    int MaxSpenderResource,
    int Currency,
    string GearJson,
    string InventoryJson,
    string LearnedRecipesJson,
    string QuestProgressJson);

public sealed record InventoryItemRecord(Guid CharacterId, string ItemCode, int Quantity);

public sealed record AccountRecord(Guid AccountId, string ExternalSubject, string DisplayName);

public sealed record AccountCharacterRecord(Guid AccountId, Guid CharacterId, int SlotIndex, string CharacterName);

public sealed record LootGrantCommand(Guid CharacterId, string PickupToken, int CurrencyDelta, string? ItemCode, int ItemQuantity);

public enum LootGrantStatus
{
    Granted = 1,
    Duplicate = 2,
    CharacterNotFound = 3,
    Invalid = 4
}

public sealed record LootGrantResult(LootGrantStatus Status);
