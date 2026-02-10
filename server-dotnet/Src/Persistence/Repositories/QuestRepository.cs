using Armament.Persistence.Abstractions;
using Armament.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace Armament.Persistence.Repositories;

public sealed class QuestRepository(ArmamentDbContext dbContext) : IQuestRepository
{
    public async Task UpsertProgressJsonAsync(Guid characterId, string questProgressJson, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Characters.SingleOrDefaultAsync(x => x.CharacterId == characterId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.QuestProgressJson = questProgressJson;
        entity.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GetProgressJsonAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var value = await dbContext.Characters.AsNoTracking()
            .Where(x => x.CharacterId == characterId)
            .Select(x => x.QuestProgressJson)
            .SingleOrDefaultAsync(cancellationToken);

        return value ?? "{}";
    }
}
