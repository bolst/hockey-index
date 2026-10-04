using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace HockeyIndex.Api.Features.Venues;

/// <summary>"Did you mean…?" (AC-VN-3): live venues within 150 m whose normalized name has trigram similarity ≥ 0.6.</summary>
public sealed class VenueDeduplicator(AppDbContext db)
{
    public const double RadiusMeters = 150;
    public const double NameSimilarityThreshold = 0.6;
    private const int MaxCandidates = 5;

    public async Task<IReadOnlyList<VenueResponse>> FindCandidatesAsync(Point location, string normalizedName, CancellationToken cancellationToken)
    {
        var matches = await db.Venues.AsNoTracking()
            .Where(venue => venue.MergedIntoId == null
                && venue.Geo.IsWithinDistance(location, RadiusMeters)
                && EF.Functions.TrigramsSimilarity(venue.NameNormalized, normalizedName) >= NameSimilarityThreshold)
            .Select(venue => new { Venue = venue, Distance = venue.Geo.Distance(location) })
            .OrderBy(match => match.Distance)
            .Take(MaxCandidates)
            .ToListAsync(cancellationToken);

        return matches.Select(match => VenueResponse.From(match.Venue, Math.Round(match.Distance))).ToList();
    }
}
