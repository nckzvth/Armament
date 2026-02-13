using Armament.Persistence.Abstractions;
using Armament.Persistence.Data;
using Armament.SharedSim.Sim;
using Microsoft.EntityFrameworkCore;

namespace Armament.Persistence.Repositories;

public sealed class CharacterRepository(ArmamentDbContext dbContext) : ICharacterRepository
{
    public async Task CreateAsync(CharacterContextRecord character, CancellationToken cancellationToken)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var entity = new CharacterEntity
        {
            CharacterId = character.CharacterId,
            Name = character.Name,
            BaseClassId = character.BaseClassId,
            SpecId = character.SpecId,
            Level = character.Level,
            Experience = character.Experience,
            Might = character.Attributes.Might,
            Will = character.Attributes.Will,
            Alacrity = character.Attributes.Alacrity,
            Constitution = character.Attributes.Constitution,
            MaxHealth = character.MaxHealth,
            MaxBuilderResource = character.MaxBuilderResource,
            MaxSpenderResource = character.MaxSpenderResource,
            Currency = character.Currency,
            GearJson = character.GearJson,
            InventoryJson = character.InventoryJson,
            LearnedRecipesJson = character.LearnedRecipesJson,
            QuestProgressJson = character.QuestProgressJson,
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow
        };

        dbContext.Characters.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CharacterContextRecord?> GetAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Characters.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CharacterId == characterId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        return new CharacterContextRecord(
            entity.CharacterId,
            entity.Name,
            entity.BaseClassId,
            entity.SpecId,
            entity.Level,
            entity.Experience,
            new CharacterAttributesRecord(entity.Might, entity.Will, entity.Alacrity, entity.Constitution),
            entity.MaxHealth,
            entity.MaxBuilderResource,
            entity.MaxSpenderResource,
            entity.Currency,
            entity.GearJson,
            entity.InventoryJson,
            entity.LearnedRecipesJson,
            entity.QuestProgressJson);
    }

    public async Task UpdateProgressionAsync(Guid characterId, int level, long experience, int currency, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Characters.SingleOrDefaultAsync(x => x.CharacterId == characterId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Level = level;
        entity.Experience = experience;
        entity.Currency = currency;
        entity.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertProfileAsync(
        Guid characterId,
        string name,
        string baseClassId,
        string specId,
        int level,
        long experience,
        int currency,
        CharacterAttributesRecord attributes,
        string inventoryJson,
        string questProgressJson,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.Characters.SingleOrDefaultAsync(x => x.CharacterId == characterId, cancellationToken);
        var attr = new CharacterAttributes
        {
            Might = attributes.Might,
            Will = attributes.Will,
            Alacrity = attributes.Alacrity,
            Constitution = attributes.Constitution
        };
        var derived = CharacterMath.ComputeDerived(attr, CharacterStatTuning.Default);
        var utcNow = DateTimeOffset.UtcNow;

        if (entity is null)
        {
            dbContext.Characters.Add(new CharacterEntity
            {
                CharacterId = characterId,
                Name = name,
                BaseClassId = baseClassId,
                SpecId = specId,
                Level = Math.Clamp(level, 1, ushort.MaxValue),
                Experience = Math.Max(0, experience),
                Might = attributes.Might,
                Will = attributes.Will,
                Alacrity = attributes.Alacrity,
                Constitution = attributes.Constitution,
                MaxHealth = derived.MaxHealth,
                MaxBuilderResource = derived.MaxBuilderResource,
                MaxSpenderResource = derived.MaxSpenderResource,
                Currency = Math.Max(0, currency),
                GearJson = "{}",
                InventoryJson = string.IsNullOrWhiteSpace(inventoryJson) ? "{}" : inventoryJson,
                LearnedRecipesJson = "[]",
                QuestProgressJson = string.IsNullOrWhiteSpace(questProgressJson) ? "{}" : questProgressJson,
                CreatedUtc = utcNow,
                UpdatedUtc = utcNow
            });
        }
        else
        {
            entity.Name = name;
            entity.BaseClassId = baseClassId;
            entity.SpecId = specId;
            entity.Level = Math.Clamp(level, 1, ushort.MaxValue);
            entity.Experience = Math.Max(0, experience);
            entity.Might = attributes.Might;
            entity.Will = attributes.Will;
            entity.Alacrity = attributes.Alacrity;
            entity.Constitution = attributes.Constitution;
            entity.MaxHealth = derived.MaxHealth;
            entity.MaxBuilderResource = derived.MaxBuilderResource;
            entity.MaxSpenderResource = derived.MaxSpenderResource;
            entity.Currency = Math.Max(0, currency);
            entity.InventoryJson = string.IsNullOrWhiteSpace(inventoryJson) ? "{}" : inventoryJson;
            entity.QuestProgressJson = string.IsNullOrWhiteSpace(questProgressJson) ? "{}" : questProgressJson;
            entity.UpdatedUtc = utcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
