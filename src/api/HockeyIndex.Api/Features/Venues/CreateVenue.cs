using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Text;
using HockeyIndex.Api.Infrastructure.Time;
using HockeyIndex.Api.Integrations.Geocoding;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Features.Venues;

/// <summary>
/// Turns a selected address suggestion into a venue (AC-VN-1, AC-VN-3, AC-VN-4): reuse by provider place id,
/// "Did you mean…?" candidates unless confirmed, then one permanent geocode and an insert serialized by an advisory lock.
/// </summary>
public sealed class CreateVenue(AppDbContext db, IGeocoder geocoder, MapboxAccess mapbox, VenueDeduplicator deduplicator, IClock clock)
{
    public const double MaxPermanentDriftMeters = 50;
    private const int VenueCreateLockClass = 7;
    private const int VenueCreateLockKey = 0;

    public async Task<IResult> HandleAsync(CreateVenueRequest request, Guid hostId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Name?.Trim() ?? string.Empty;
        var normalizedName = TextNormalizer.Normalize(name);
        var providerPlaceId = request.ProviderPlaceId?.Trim() ?? string.Empty;
        var address = request.Address?.Trim() ?? string.Empty;
        if (ValidationError(name, normalizedName, providerPlaceId, address, request.Latitude, request.Longitude) is { } invalid)
        {
            return invalid;
        }

        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;

        if (await FindByProviderPlaceIdAsync(providerPlaceId, cancellationToken) is { } existing)
        {
            return TypedResults.Ok(VenueResponse.From(existing));
        }

        if (!request.ConfirmNew)
        {
            var candidates = await deduplicator.FindCandidatesAsync(VenueGeometry.Point(latitude, longitude), normalizedName, cancellationToken);
            if (candidates.Count > 0)
            {
                return VenueProblems.Candidates(candidates);
            }
        }

        if (await mapbox.IsBreakerOpenAsync(cancellationToken))
        {
            return VenueProblems.ProviderUnavailable();
        }

        if (!await mapbox.TryChargeAsync(ProviderNames.MapboxPerm, hostId, cancellationToken))
        {
            return VenueProblems.LimitReached();
        }

        var geocoded = await geocoder.GeocodePermanentAsync(address, cancellationToken);
        if (!geocoded.Succeeded)
        {
            if (!geocoded.IsProviderFault)
            {
                return VenueProblems.AddressNotFound();
            }

            await mapbox.OpenBreakerAsync(cancellationToken);
            return VenueProblems.ProviderUnavailable();
        }

        var confirmed = geocoded.Addresses.FirstOrDefault(candidate =>
            VenueGeometry.DistanceMeters(latitude, longitude, candidate.Latitude, candidate.Longitude) <= MaxPermanentDriftMeters);
        if (confirmed is null)
        {
            return geocoded.Addresses.Count == 0 ? VenueProblems.AddressNotFound() : VenueProblems.LocationMismatch();
        }

        if (TimeZoneLookup.Find(confirmed.Latitude, confirmed.Longitude) is not { } timeZone)
        {
            return VenueProblems.TimeZoneUnknown();
        }

        return await InsertAsync(name, normalizedName, providerPlaceId, confirmed, timeZone, hostId, request.ConfirmNew, cancellationToken);
    }

    private async Task<IResult> InsertAsync(
        string name,
        string normalizedName,
        string suggestionPlaceId,
        GeocodedAddress confirmed,
        string timeZone,
        Guid hostId,
        bool confirmNew,
        CancellationToken cancellationToken)
    {
        var location = VenueGeometry.Point(confirmed.Latitude, confirmed.Longitude);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({VenueCreateLockClass}, {VenueCreateLockKey})", cancellationToken);

        if (await FindByProviderPlaceIdAsync(confirmed.ProviderPlaceId, cancellationToken) is { } byPermanentId)
        {
            return TypedResults.Ok(VenueResponse.From(byPermanentId));
        }

        if (await FindByProviderPlaceIdAsync(suggestionPlaceId, cancellationToken) is { } bySuggestionId)
        {
            return TypedResults.Ok(VenueResponse.From(bySuggestionId));
        }

        if (!confirmNew)
        {
            var candidates = await deduplicator.FindCandidatesAsync(location, normalizedName, cancellationToken);
            if (candidates.Count > 0)
            {
                return VenueProblems.Candidates(candidates);
            }
        }

        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        var venue = new Venue
        {
            Id = Guid.CreateVersion7(),
            PublicId = PublicId.New(),
            Name = name,
            NameNormalized = normalizedName,
            AddressLine = confirmed.AddressLine,
            City = confirmed.City,
            Region = confirmed.Region,
            Country = confirmed.Country,
            Geo = location,
            TimeZone = timeZone,
            Provider = VenueProviders.Mapbox,
            ProviderPlaceId = confirmed.ProviderPlaceId,
            CreatedBy = hostId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Venues.Add(venue);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var response = VenueResponse.From(venue);
        return TypedResults.Created($"/v1/venues/{venue.PublicId}", response);
    }

    private async Task<Venue?> FindByProviderPlaceIdAsync(string providerPlaceId, CancellationToken cancellationToken)
    {
        var venue = await db.Venues.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Provider == VenueProviders.Mapbox && candidate.ProviderPlaceId == providerPlaceId, cancellationToken);
        return venue is null ? null : await VenueMerges.FollowAsync(db, venue, cancellationToken);
    }

    private static IResult? ValidationError(
        string name, string normalizedName, string providerPlaceId, string address, double? latitude, double? longitude)
    {
        if (normalizedName.Length == 0 || name.Length > Venue.NameMaxLength)
        {
            return VenueProblems.InvalidVenue($"Name must be 1–{Venue.NameMaxLength} characters.");
        }

        if (providerPlaceId.Length is 0 or > Venue.ProviderPlaceIdMaxLength)
        {
            return VenueProblems.InvalidVenue("Select an address from the suggestions.");
        }

        if (address.Length is 0 or > Venue.AddressLineMaxLength * 2)
        {
            return VenueProblems.InvalidVenue("Select an address from the suggestions.");
        }

        if (latitude is not { } lat || longitude is not { } lng || !VenueGeometry.IsValid(lat, lng))
        {
            return VenueProblems.InvalidVenue("Select an address from the suggestions.");
        }

        return null;
    }
}
