using Armament.Persistence.Abstractions;
using Armament.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace Armament.Persistence.Services;

public sealed class LootTransactionService(ArmamentDbContext dbContext) : ILootTransactionService
{
    public async Task<LootGrantResult> TryGrantLootAsync(LootGrantCommand command, CancellationToken cancellationToken)
    {
        if (command.CharacterId == Guid.Empty || string.IsNullOrWhiteSpace(command.PickupToken))
        {
            return new LootGrantResult(LootGrantStatus.Invalid);
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var character = await dbContext.Characters.SingleOrDefaultAsync(x => x.CharacterId == command.CharacterId, cancellationToken);
        if (character is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return new LootGrantResult(LootGrantStatus.CharacterNotFound);
        }

        try
        {
            dbContext.LootClaims.Add(new LootClaimEntity
            {
                CharacterId = command.CharacterId,
                PickupToken = command.PickupToken,
                ClaimedAtUtc = DateTimeOffset.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            await tx.RollbackAsync(cancellationToken);
            return new LootGrantResult(LootGrantStatus.Duplicate);
        }

        character.Currency += command.CurrencyDelta;
        character.UpdatedUtc = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(command.ItemCode) && command.ItemQuantity > 0)
        {
            var item = await dbContext.InventoryItems.SingleOrDefaultAsync(
                x => x.CharacterId == command.CharacterId && x.ItemCode == command.ItemCode,
                cancellationToken);

            if (item is null)
            {
                dbContext.InventoryItems.Add(new InventoryItemEntity
                {
                    CharacterId = command.CharacterId,
                    ItemCode = command.ItemCode,
                    Quantity = command.ItemQuantity
                });
            }
            else
            {
                item.Quantity += command.ItemQuantity;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new LootGrantResult(LootGrantStatus.Granted);
    }
}
