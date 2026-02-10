using Armament.Persistence.Abstractions;
using Armament.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace Armament.Persistence.Repositories;

public sealed class AccountRepository(ArmamentDbContext dbContext) : IAccountRepository
{
    public async Task<AccountRecord> GetOrCreateBySubjectAsync(string externalSubject, string displayName, CancellationToken cancellationToken)
    {
        var subject = externalSubject.Trim();
        if (subject.Length == 0)
        {
            throw new ArgumentException("externalSubject is required", nameof(externalSubject));
        }

        var existing = await dbContext.Accounts.SingleOrDefaultAsync(x => x.ExternalSubject == subject, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(displayName))
            {
                existing.DisplayName = displayName;
                existing.UpdatedUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return new AccountRecord(existing.AccountId, existing.ExternalSubject, existing.DisplayName);
        }

        var account = new AccountEntity
        {
            AccountId = Guid.NewGuid(),
            ExternalSubject = subject,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? subject : displayName,
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AccountRecord(account.AccountId, account.ExternalSubject, account.DisplayName);
    }

    public async Task BindCharacterAsync(Guid accountId, Guid characterId, int slotIndex, string characterName, CancellationToken cancellationToken)
    {
        var existing = await dbContext.AccountCharacters
            .SingleOrDefaultAsync(x => x.AccountId == accountId && x.CharacterId == characterId, cancellationToken);

        if (existing is null)
        {
            dbContext.AccountCharacters.Add(new AccountCharacterEntity
            {
                AccountId = accountId,
                CharacterId = characterId,
                SlotIndex = slotIndex,
                CharacterName = characterName,
                CreatedUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.SlotIndex = slotIndex;
            existing.CharacterName = characterName;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccountCharacterRecord>> ListCharactersAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await dbContext.AccountCharacters
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderBy(x => x.SlotIndex)
            .Select(x => new AccountCharacterRecord(x.AccountId, x.CharacterId, x.SlotIndex, x.CharacterName))
            .ToListAsync(cancellationToken);
    }
}
