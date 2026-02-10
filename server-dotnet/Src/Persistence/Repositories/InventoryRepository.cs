using Armament.Persistence.Abstractions;
using Armament.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace Armament.Persistence.Repositories;

public sealed class InventoryRepository(ArmamentDbContext dbContext) : IInventoryRepository
{
    public async Task<IReadOnlyList<InventoryItemRecord>> ListAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var records = await dbContext.InventoryItems.AsNoTracking()
            .Where(x => x.CharacterId == characterId)
            .OrderBy(x => x.ItemCode)
            .Select(x => new InventoryItemRecord(x.CharacterId, x.ItemCode, x.Quantity))
            .ToListAsync(cancellationToken);

        return records;
    }

    public async Task AddOrIncrementAsync(Guid characterId, string itemCode, int quantity, CancellationToken cancellationToken)
    {
        if (quantity <= 0)
        {
            return;
        }

        var entity = await dbContext.InventoryItems.SingleOrDefaultAsync(
            x => x.CharacterId == characterId && x.ItemCode == itemCode,
            cancellationToken);

        if (entity is null)
        {
            entity = new InventoryItemEntity
            {
                CharacterId = characterId,
                ItemCode = itemCode,
                Quantity = quantity
            };
            dbContext.InventoryItems.Add(entity);
        }
        else
        {
            entity.Quantity += quantity;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
