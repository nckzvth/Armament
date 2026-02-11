using Armament.GameServer.Persistence;
using Armament.Persistence;
using Armament.Persistence.Abstractions;
using Armament.SharedSim.Sim;

namespace Armament.ServerHost;

public sealed class PersistenceBackedLootSink : IAsyncDisposable
{
    private readonly BoundedLootPersistenceQueue _queue;
    private readonly string _connectionString;
    private readonly string _serverSessionNonce = Guid.NewGuid().ToString("N");

    public PersistenceBackedLootSink(string connectionString, int capacity = 1024)
    {
        _connectionString = connectionString;
        _queue = new BoundedLootPersistenceQueue(capacity, HandleAsync);
    }

    public ILootPersistenceSink Sink => _queue;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = PersistenceModule.CreateDbContext(_connectionString);
        await PersistenceModule.ApplyMigrationsAsync(dbContext, cancellationToken);
    }

    private async Task HandleAsync(LootPersistenceRequest request, CancellationToken cancellationToken)
    {
        await using var dbContext = PersistenceModule.CreateDbContext(_connectionString);
        var (characters, _, _, loot) = PersistenceModule.CreateRepositories(dbContext);

        // Include a per-process session nonce so claim IDs cannot collide across server restarts.
        var pickupToken = $"{_serverSessionNonce}:{request.ZoneName}:{request.InstanceId}:{request.ServerTick}:{request.LootId}:{request.CharacterId}";
        var grant = new LootGrantCommand(
            CharacterId: request.CharacterId,
            PickupToken: pickupToken,
            CurrencyDelta: request.CurrencyDelta,
            ItemCode: null,
            ItemQuantity: 0);

        var result = await loot.TryGrantLootAsync(grant, cancellationToken);
        if (result.Status == LootGrantStatus.CharacterNotFound)
        {
            var derived = CharacterMath.ComputeDerived(CharacterAttributes.Default, CharacterStatTuning.Default);
            var character = new CharacterContextRecord(
                CharacterId: request.CharacterId,
                Name: request.CharacterName,
                BaseClassId: "bastion",
                SpecId: "spec.bastion.bulwark",
                Level: 1,
                Experience: 0,
                Attributes: new CharacterAttributesRecord(
                    CharacterAttributes.Default.Might,
                    CharacterAttributes.Default.Will,
                    CharacterAttributes.Default.Alacrity,
                    CharacterAttributes.Default.Constitution),
                MaxHealth: derived.MaxHealth,
                MaxBuilderResource: derived.MaxBuilderResource,
                MaxSpenderResource: derived.MaxSpenderResource,
                Currency: 0,
                GearJson: "{}",
                InventoryJson: "{}",
                LearnedRecipesJson: "[]",
                QuestProgressJson: "{}");

            await characters.CreateAsync(character, cancellationToken);
            _ = await loot.TryGrantLootAsync(grant, cancellationToken);
        }
    }

    public long ProcessedCount => _queue.ProcessedCount;
    public long DroppedCount => _queue.DroppedCount;
    public long FailedCount => _queue.FailedCount;

    public ValueTask DisposeAsync()
    {
        return _queue.DisposeAsync();
    }
}
