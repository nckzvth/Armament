namespace Armament.GameServer.Persistence;

public sealed record LootPersistenceRequest(
    Guid CharacterId,
    string CharacterName,
    uint ClientId,
    uint PlayerEntityId,
    uint LootId,
    int CurrencyDelta,
    bool AutoLoot,
    uint ServerTick,
    uint InstanceId,
    string ZoneName);

public interface ILootPersistenceSink
{
    bool TryEnqueue(LootPersistenceRequest request);
}
