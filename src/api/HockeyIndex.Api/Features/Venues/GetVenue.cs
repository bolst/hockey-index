using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HockeyIndex.Api.Features.Venues;

public sealed class GetVenue(AppDbContext db)
{
    public async Task<IResult> HandleAsync(string publicId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publicId);
        if (publicId.Length != PublicId.Length)
        {
            return VenueProblems.NotFound();
        }

        var venue = await db.Venues.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.PublicId == publicId, cancellationToken);
        return venue is null
            ? VenueProblems.NotFound()
            : TypedResults.Ok(VenueResponse.From(await VenueMerges.FollowAsync(db, venue, cancellationToken)));
    }
}
