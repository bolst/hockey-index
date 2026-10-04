using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HockeyIndex.Api.Features.Events;

public sealed class DeleteDraft(AppDbContext db)
{
    public async Task<IResult> HandleAsync(string publicId, Guid hostId, CancellationToken cancellationToken)
    {
        if (await EventRows.FindOwnedAsync(db, publicId, hostId, cancellationToken) is not { } snapshot)
        {
            return EventProblems.NotFound();
        }

        var deleted = await db.Events
            .Where(evt => evt.Id == snapshot.Id && evt.Status == EventStatus.Draft)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? EventProblems.NotDraft() : TypedResults.NoContent();
    }
}
