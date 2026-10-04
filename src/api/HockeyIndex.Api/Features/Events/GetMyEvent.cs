using HockeyIndex.Api.Infrastructure.Persistence;

namespace HockeyIndex.Api.Features.Events;

public sealed class GetMyEvent(AppDbContext db, EventResponder responder)
{
    public async Task<IResult> HandleAsync(string publicId, Guid hostId, HttpResponse response, CancellationToken cancellationToken) =>
        await EventRows.FindOwnedAsync(db, publicId, hostId, cancellationToken) is { } evt
            ? TypedResults.Ok(await responder.ToResponseAsync(evt, response, cancellationToken))
            : EventProblems.NotFound();
}
