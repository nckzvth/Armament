using Armament.Persistence;
using Armament.Persistence.Abstractions;
using Armament.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

var failures = new List<string>();

void Assert(bool condition, string message)
{
    if (!condition)
    {
        failures.Add(message);
    }
}

var connectionString = await ResolveConnectionStringAsync();
if (connectionString is null)
{
    if (string.Equals(Environment.GetEnvironmentVariable("ARMAMENT_ALLOW_PERSISTENCE_SKIP"), "1", StringComparison.Ordinal))
    {
        Console.WriteLine("Persistence integration tests skipped: Docker unavailable and ARMAMENT_TEST_DB_CONNECTION not set.");
        return 0;
    }

    Console.Error.WriteLine("Persistence integration tests failed: Docker unavailable and ARMAMENT_TEST_DB_CONNECTION not set.");
    return 1;
}

await using var dbContext = PersistenceModule.CreateDbContext(connectionString);
await PersistenceModule.ApplyMigrationsAsync(dbContext, CancellationToken.None);

var (characters, inventory, _, lootService) = PersistenceModule.CreateRepositories(dbContext);
var accounts = PersistenceModule.CreateAccountRepository(dbContext);

var characterId = Guid.NewGuid();
var character = new CharacterContextRecord(
    CharacterId: characterId,
    Name: "PersistenceTester",
    Level: 1,
    Experience: 0,
    Attributes: new CharacterAttributesRecord(10, 10, 10, 10),
    MaxHealth: 100,
    MaxBuilderResource: 100,
    MaxSpenderResource: 100,
    Currency: 5,
    GearJson: "{}",
    InventoryJson: "{}",
    LearnedRecipesJson: "[]",
    QuestProgressJson: "{}");

await characters.CreateAsync(character, CancellationToken.None);

var loaded = await characters.GetAsync(characterId, CancellationToken.None);
Assert(loaded is not null, "created character was not found");
Assert(loaded is not null && loaded.Currency == 5, "initial currency mismatch");

var pickupToken = "drop:overworld:enemy42:tick100";
var grantCommand = new LootGrantCommand(
    CharacterId: characterId,
    PickupToken: pickupToken,
    CurrencyDelta: 7,
    ItemCode: "test_relic",
    ItemQuantity: 1);

var firstGrant = await lootService.TryGrantLootAsync(grantCommand, CancellationToken.None);
Assert(firstGrant.Status == LootGrantStatus.Granted, "first loot grant should be granted");

var secondGrant = await lootService.TryGrantLootAsync(grantCommand, CancellationToken.None);
Assert(secondGrant.Status == LootGrantStatus.Duplicate, "duplicate loot grant should be rejected");

loaded = await characters.GetAsync(characterId, CancellationToken.None);
Assert(loaded is not null && loaded.Currency == 12, "currency should increment only once");

var inventoryRows = await inventory.ListAsync(characterId, CancellationToken.None);
var relic = inventoryRows.SingleOrDefault(x => x.ItemCode == "test_relic");
Assert(relic is not null, "inventory item missing after grant");
Assert(relic is not null && relic.Quantity == 1, "inventory item quantity should be 1 without duplication");

var migrationRowCount = await CountMigrationsAsync(dbContext);
Assert(migrationRowCount > 0, "no migration entries were applied");

var account = await accounts.GetOrCreateBySubjectAsync("local:test-user-1", "TestUser", CancellationToken.None);
await accounts.BindCharacterAsync(account.AccountId, characterId, slotIndex: 0, characterName: "PersistenceTester", CancellationToken.None);

var secondCharacterId = Guid.NewGuid();
await characters.UpsertProfileAsync(
    secondCharacterId,
    "AltCharacter",
    level: 3,
    experience: 120,
    currency: 77,
    new CharacterAttributesRecord(12, 8, 10, 9),
    CancellationToken.None);
await accounts.BindCharacterAsync(account.AccountId, secondCharacterId, slotIndex: 1, characterName: "AltCharacter", CancellationToken.None);

var roster = await accounts.ListCharactersAsync(account.AccountId, CancellationToken.None);
Assert(roster.Count == 2, "account roster should contain two characters");
Assert(roster[0].SlotIndex == 0 && roster[1].SlotIndex == 1, "account roster slot ordering mismatch");

var loadedPrimary = await characters.GetAsync(characterId, CancellationToken.None);
var loadedSecondary = await characters.GetAsync(secondCharacterId, CancellationToken.None);
Assert(loadedPrimary is not null && loadedSecondary is not null, "linked account characters not found");
Assert(loadedPrimary is not null && loadedSecondary is not null && loadedPrimary.Currency != loadedSecondary.Currency, "multi-character currency isolation failed");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Persistence tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($" - {failure}");
    }

    return 1;
}

Console.WriteLine("Persistence integration tests passed.");
return 0;

static async Task<string?> ResolveConnectionStringAsync()
{
    var explicitConnection = Environment.GetEnvironmentVariable("ARMAMENT_TEST_DB_CONNECTION");
    if (!string.IsNullOrWhiteSpace(explicitConnection))
    {
        return explicitConnection;
    }

    PostgreSqlContainer? pgContainer = null;
    try
    {
        pgContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("armament_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await pgContainer.StartAsync();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => pgContainer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return pgContainer.GetConnectionString();
    }
    catch
    {
        if (pgContainer is not null)
        {
            await pgContainer.DisposeAsync();
        }

        return null;
    }
}

static async Task<int> CountMigrationsAsync(ArmamentDbContext dbContext)
{
    await using var command = dbContext.Database.GetDbConnection().CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"";

    if (command.Connection is not null && command.Connection.State != System.Data.ConnectionState.Open)
    {
        await command.Connection.OpenAsync();
    }

    var scalar = await command.ExecuteScalarAsync();
    return scalar is null ? 0 : Convert.ToInt32(scalar);
}
