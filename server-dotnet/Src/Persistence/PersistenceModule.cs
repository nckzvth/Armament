using Armament.Persistence.Abstractions;
using Armament.Persistence.Data;
using Armament.Persistence.Repositories;
using Armament.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Armament.Persistence;

public static class PersistenceModule
{
    public static ArmamentDbContext CreateDbContext(string connectionString)
    {
        var options = DbContextFactory.BuildOptions(connectionString);
        return new ArmamentDbContext(options);
    }

    public static (ICharacterRepository Characters, IInventoryRepository Inventory, IQuestRepository Quests, ILootTransactionService Loot)
        CreateRepositories(ArmamentDbContext dbContext)
    {
        return (
            new CharacterRepository(dbContext),
            new InventoryRepository(dbContext),
            new QuestRepository(dbContext),
            new LootTransactionService(dbContext));
    }

    public static IAccountRepository CreateAccountRepository(ArmamentDbContext dbContext)
    {
        return new AccountRepository(dbContext);
    }

    public static Task ApplyMigrationsAsync(ArmamentDbContext dbContext, CancellationToken cancellationToken)
    {
        return dbContext.Database.MigrateAsync(cancellationToken);
    }
}
