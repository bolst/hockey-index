using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HockeyIndex.Api.Features.Venues;

public static class VenueMerges
{
    private const int MaxHops = 8;

    /// <summary>Resolves a merged venue to the venue it was merged into.</summary>
    public static async Task<Venue> FollowAsync(AppDbContext db, Venue venue, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(venue);

        var current = venue;
        for (var hop = 0; hop < MaxHops && current.MergedIntoId is { } targetId; hop++)
        {
            var target = await db.Venues.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == targetId, cancellationToken);
            if (target is null)
            {
                break;
            }

            current = target;
        }

        return current;
    }
}
