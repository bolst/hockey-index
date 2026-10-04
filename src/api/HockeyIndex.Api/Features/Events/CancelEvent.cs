using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;

namespace HockeyIndex.Api.Features.Events;

/// <summary>Published → cancelled (AC-LC-4). The event stays in search with a CANCELLED badge until it ends.</summary>
public sealed class CancelEvent(AppDbContext db, EventTransitions transitions, EventResponder responder)
{
    public async Task<IResult> HandleAsync(string publicId, Guid hostId, HttpResponse response, CancellationToken cancellationToken)
    {
        if (await EventRows.FindOwnedAsync(db, publicId, hostId, cancellationToken) is not { } snapshot)
        {
            return EventProblems.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var evt = await EventRows.LockAsync(db, snapshot.Id, cancellationToken);
        if (evt is null)
        {
            return EventProblems.NotFound();
        }

        if (evt.Status != EventStatus.Published)
        {
            return EventProblems.InvalidTransition();
        }

        transitions.Cancel(evt, TransitionActor.Host(hostId));
        await transitions.CommitAsync(transaction, cancellationToken);
        return TypedResults.Ok(await responder.ToResponseAsync(evt, response, cancellationToken));
    }
}
