namespace Armament.Persistence.Data;

public sealed class AccountEntity
{
    public Guid AccountId { get; set; }
    public string ExternalSubject { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class AccountCharacterEntity
{
    public long AccountCharacterId { get; set; }
    public Guid AccountId { get; set; }
    public Guid CharacterId { get; set; }
    public int SlotIndex { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }

    public AccountEntity Account { get; set; } = null!;
    public CharacterEntity Character { get; set; } = null!;
}

public sealed class CharacterEntity
{
    public Guid CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseClassId { get; set; } = "bastion";
    public string SpecId { get; set; } = "spec.bastion.bulwark";
    public int Level { get; set; }
    public long Experience { get; set; }

    public int Might { get; set; }
    public int Will { get; set; }
    public int Alacrity { get; set; }
    public int Constitution { get; set; }

    public int MaxHealth { get; set; }
    public int MaxBuilderResource { get; set; }
    public int MaxSpenderResource { get; set; }
    public int Currency { get; set; }

    public string GearJson { get; set; } = "{}";
    public string InventoryJson { get; set; } = "{}";
    public string LearnedRecipesJson { get; set; } = "[]";
    public string QuestProgressJson { get; set; } = "{}";

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public List<InventoryItemEntity> InventoryItems { get; set; } = new();
    public List<AccountCharacterEntity> AccountCharacters { get; set; } = new();
}

public sealed class InventoryItemEntity
{
    public long InventoryItemId { get; set; }
    public Guid CharacterId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public int Quantity { get; set; }

    public CharacterEntity Character { get; set; } = null!;
}

public sealed class QuestProgressEntity
{
    public long QuestProgressId { get; set; }
    public Guid CharacterId { get; set; }
    public string QuestCode { get; set; } = string.Empty;
    public int ProgressValue { get; set; }

    public CharacterEntity Character { get; set; } = null!;
}

public sealed class LearnedRecipeEntity
{
    public long LearnedRecipeId { get; set; }
    public Guid CharacterId { get; set; }
    public string RecipeCode { get; set; } = string.Empty;

    public CharacterEntity Character { get; set; } = null!;
}

public sealed class LootClaimEntity
{
    public long LootClaimId { get; set; }
    public Guid CharacterId { get; set; }
    public string PickupToken { get; set; } = string.Empty;
    public DateTimeOffset ClaimedAtUtc { get; set; }

    public CharacterEntity Character { get; set; } = null!;
}
