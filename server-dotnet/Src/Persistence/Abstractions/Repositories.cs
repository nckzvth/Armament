using System.Threading;
using System.Threading.Tasks;

namespace Armament.Persistence.Abstractions;

public interface ICharacterRepository
{
    Task CreateAsync(CharacterContextRecord character, CancellationToken cancellationToken);
    Task<CharacterContextRecord?> GetAsync(Guid characterId, CancellationToken cancellationToken);
    Task UpdateProgressionAsync(Guid characterId, int level, long experience, int currency, CancellationToken cancellationToken);
    Task UpsertProfileAsync(
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
        CancellationToken cancellationToken);
}

public interface IInventoryRepository
{
    Task<IReadOnlyList<InventoryItemRecord>> ListAsync(Guid characterId, CancellationToken cancellationToken);
    Task AddOrIncrementAsync(Guid characterId, string itemCode, int quantity, CancellationToken cancellationToken);
}

public interface IQuestRepository
{
    Task UpsertProgressJsonAsync(Guid characterId, string questProgressJson, CancellationToken cancellationToken);
    Task<string> GetProgressJsonAsync(Guid characterId, CancellationToken cancellationToken);
}

public interface ILootTransactionService
{
    Task<LootGrantResult> TryGrantLootAsync(LootGrantCommand command, CancellationToken cancellationToken);
}

public interface IAccountRepository
{
    Task<AccountRecord> GetOrCreateBySubjectAsync(string externalSubject, string displayName, CancellationToken cancellationToken);
    Task BindCharacterAsync(Guid accountId, Guid characterId, int slotIndex, string characterName, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountCharacterRecord>> ListCharactersAsync(Guid accountId, CancellationToken cancellationToken);
}
