using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Text;
using HockeyIndex.Api.Integrations.Geocoding;
using Microsoft.EntityFrameworkCore;

namespace HockeyIndex.Api.Features.Venues;

/// <summary>
/// AC-VN-2: venues from the database first (trigram match on the normalized name); then, only when fewer than
/// <see cref="Limit"/> venues match, temporary Mapbox address suggestions within the breaker and the <c>mapbox_temp</c> budget.
/// </summary>
public sealed class AutocompleteVenues(AppDbContext db, IGeocoder geocoder, MapboxAccess mapbox)
{
    public const int Limit = 5;
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = Venue.NameMaxLength;
    public const string SimilarityThreshold = "0.3";

    public async Task<IResult> HandleAsync(string? query, double? latitude, double? longitude, Guid hostId, CancellationToken cancellationToken)
    {
        var normalizedQuery = TextNormalizer.Normalize(query);
        if (normalizedQuery.Length is < MinQueryLength or > MaxQueryLength || !TryReadNear(latitude, longitude, out var near))
        {
            return VenueProblems.InvalidQuery();
        }

        var venues = await FindVenuesAsync(normalizedQuery, near, cancellationToken);
        if (venues.Count >= Limit)
        {
            return TypedResults.Ok(new AutocompleteVenuesResponse(venues, [], SuggestionsStatus.NotNeeded));
        }

        var (suggestions, status) = await SuggestAsync(query!.Trim(), near, hostId, cancellationToken);
        return TypedResults.Ok(new AutocompleteVenuesResponse(venues, suggestions, status));
    }

    private async Task<IReadOnlyList<VenueResponse>> FindVenuesAsync(string normalizedQuery, GeoCoordinate? near, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"SELECT set_config('pg_trgm.similarity_threshold', {SimilarityThreshold}, true)",
            cancellationToken);

        var matching = db.Venues.AsNoTracking()
            .Where(venue => venue.MergedIntoId == null && EF.Functions.TrigramsAreSimilar(venue.NameNormalized, normalizedQuery));

        IReadOnlyList<VenueResponse> venues;
        if (near is null)
        {
            venues = (await matching
                    .OrderByDescending(venue => EF.Functions.TrigramsSimilarity(venue.NameNormalized, normalizedQuery))
                    .ThenBy(venue => venue.Name)
                    .Take(Limit)
                    .ToListAsync(cancellationToken))
                .Select(venue => VenueResponse.From(venue))
                .ToList();
        }
        else
        {
            var point = VenueGeometry.Point(near.Latitude, near.Longitude);
            venues = (await matching
                    .Select(venue => new { Venue = venue, Distance = venue.Geo.Distance(point) })
                    .OrderByDescending(match => EF.Functions.TrigramsSimilarity(match.Venue.NameNormalized, normalizedQuery))
                    .ThenBy(match => match.Distance)
                    .Take(Limit)
                    .ToListAsync(cancellationToken))
                .Select(match => VenueResponse.From(match.Venue, Math.Round(match.Distance)))
                .ToList();
        }

        await transaction.CommitAsync(cancellationToken);
        return venues;
    }

    private async Task<(IReadOnlyList<AddressSuggestion> Suggestions, string Status)> SuggestAsync(
        string query, GeoCoordinate? near, Guid hostId, CancellationToken cancellationToken)
    {
        if (await mapbox.IsBreakerOpenAsync(cancellationToken))
        {
            return ([], SuggestionsStatus.Unavailable);
        }

        if (!await mapbox.TryChargeAsync(ProviderNames.MapboxTemp, hostId, cancellationToken))
        {
            return ([], SuggestionsStatus.LimitReached);
        }

        var result = await geocoder.SuggestAsync(query, near, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.IsProviderFault)
            {
                await mapbox.OpenBreakerAsync(cancellationToken);
            }

            return ([], SuggestionsStatus.Unavailable);
        }

        return (result.Addresses.Select(AddressSuggestion.From).ToList(), SuggestionsStatus.Ok);
    }

    private static bool TryReadNear(double? latitude, double? longitude, out GeoCoordinate? near)
    {
        near = null;
        if (latitude is null && longitude is null)
        {
            return true;
        }

        if (latitude is not { } lat || longitude is not { } lng || !VenueGeometry.IsValid(lat, lng))
        {
            return false;
        }

        near = new GeoCoordinate(lat, lng);
        return true;
    }
}
