using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Features.Venues;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Text;
using HockeyIndex.Api.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Features.Admin;

/// <summary>
/// Venue search, edit and merge (AC-VN-5). A time zone change re-derives the UTC instants of the venue's non-archived events;
/// a wall time that does not exist in the new zone refuses the change. Changed non-draft events are purged after commit.
/// </summary>
public sealed class ManageVenues(AppDbContext db, EventTransitions transitions, AuditRecorder audit, VenueTimeConverter times, IClock clock)
{
    public const int SearchLimit = 50;

    public async Task<IReadOnlyList<VenueResponse>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        var venues = db.Venues.AsNoTracking().Where(venue => venue.MergedIntoId == null);
        var normalized = TextNormalizer.Normalize(query);
        if (normalized.Length > 0)
        {
            var pattern = $"%{normalized.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
            venues = venues.Where(venue => EF.Functions.Like(venue.NameNormalized, pattern));
        }

        return [.. (await venues.OrderBy(venue => venue.NameNormalized).ThenBy(venue => venue.Id).Take(SearchLimit).ToListAsync(cancellationToken)).Select(venue => VenueResponse.From(venue))];
    }

    public async Task<IResult> UpdateAsync(string publicId, VenueEditRequest request, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await LockVenueAsync(publicId, cancellationToken) is not { } venue)
        {
            return VenueProblems.NotFound();
        }

        var before = VenueResponse.From(venue);
        if (Apply(venue, request) is { } invalid)
        {
            return invalid;
        }

        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        venue.UpdatedAt = now;
        var events = await LockEventsAsync(venue.Id, cancellationToken);
        if (Rederive(events, venue.TimeZone, now) is { } unresolvable)
        {
            return unresolvable;
        }

        audit.Record(adminId, "venue.update", AuditTargets.Venue, venue.PublicId, reason, new { Before = before, After = VenueResponse.From(venue) });
        await transitions.CommitAsync(transaction, cancellationToken);
        return TypedResults.Ok(VenueResponse.From(venue));
    }

    /// <summary>Points the source at the target (following earlier merges) and moves every event to the target.</summary>
    public async Task<IResult> MergeAsync(string publicId, VenueMergeRequest request, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var source = await LockVenueAsync(publicId, cancellationToken);
        var requestedTarget = await db.Venues.AsNoTracking().SingleOrDefaultAsync(venue => venue.PublicId == request.IntoId, cancellationToken);
        if (source is null || requestedTarget is null)
        {
            return VenueProblems.NotFound();
        }

        var target = await VenueMerges.FollowAsync(db, requestedTarget, cancellationToken);
        if (source.MergedIntoId is not null)
        {
            return AdminProblems.VenueMergeInvalid("This venue was already merged.");
        }

        if (target.Id == source.Id || target.MergedIntoId is not null)
        {
            return AdminProblems.VenueMergeInvalid("A venue cannot be merged into itself.");
        }

        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        source.MergedIntoId = target.Id;
        source.UpdatedAt = now;
        var events = await LockEventsAsync(source.Id, cancellationToken, includeArchived: true);
        foreach (var evt in events)
        {
            evt.VenueId = target.Id;
            evt.UpdatedAt = now;
            if (evt.Status != EventStatus.Draft)
            {
                transitions.PurgeAfterCommit(evt);
            }
        }

        if (Rederive([.. events.Where(evt => evt.Status != EventStatus.Archived)], target.TimeZone, now) is { } unresolvable)
        {
            return unresolvable;
        }

        audit.Record(
            adminId, "venue.merge", AuditTargets.Venue, source.PublicId, reason,
            new { IntoId = target.PublicId, EventsMoved = events.Count });
        await transitions.CommitAsync(transaction, cancellationToken);
        return TypedResults.Ok(VenueResponse.From(target));
    }

    private static IResult? Apply(Venue venue, VenueEditRequest request)
    {
        if (request.Name is { } name)
        {
            var trimmed = name.Trim();
            var normalized = TextNormalizer.Normalize(trimmed);
            if (normalized.Length == 0 || trimmed.Length > Venue.NameMaxLength)
            {
                return VenueProblems.InvalidVenue($"Name must be 1 to {Venue.NameMaxLength} characters.");
            }

            venue.Name = trimmed;
            venue.NameNormalized = normalized;
        }

        if (request.AddressLine is { } addressLine)
        {
            if (addressLine.Trim() is not { Length: > 0 and <= Venue.AddressLineMaxLength } trimmed)
            {
                return VenueProblems.InvalidVenue($"Address must be 1 to {Venue.AddressLineMaxLength} characters.");
            }

            venue.AddressLine = trimmed;
        }

        if (request.City is { } city)
        {
            if (city.Trim() is not { Length: > 0 and <= Venue.CityMaxLength } trimmed)
            {
                return VenueProblems.InvalidVenue($"City must be 1 to {Venue.CityMaxLength} characters.");
            }

            venue.City = trimmed;
        }

        if (request.Region is { } region)
        {
            if (region.Trim() is not { Length: 2 } trimmed)
            {
                return VenueProblems.InvalidVenue("Region must be a 2-letter code.");
            }

            venue.Region = trimmed.ToUpperInvariant();
        }

        if (request.Country is { } country)
        {
            var code = country.Trim().ToUpperInvariant();
            if (code is not ("CA" or "US"))
            {
                return VenueProblems.InvalidVenue("Country must be CA or US.");
            }

            venue.Country = code;
        }

        if (request.Latitude is not null || request.Longitude is not null)
        {
            if (request.Latitude is not { } latitude || request.Longitude is not { } longitude || !VenueGeometry.IsValid(latitude, longitude))
            {
                return VenueProblems.InvalidVenue("Give both a valid latitude and longitude.");
            }

            venue.Geo = VenueGeometry.Point(latitude, longitude);
        }

        if (request.TimeZone is { } timeZone)
        {
            if (TimeZoneLookup.Canonicalize(timeZone.Trim()) is not { } canonical)
            {
                return VenueProblems.TimeZoneUnknown();
            }

            venue.TimeZone = canonical;
        }

        return null;
    }

    private IResult? Rederive(IReadOnlyList<HockeyEvent> events, string timeZone, DateTimeOffset now)
    {
        foreach (var evt in events.Where(evt => evt.Status != EventStatus.Archived))
        {
            var starts = times.ToInstant(timeZone, evt.StartsLocal);
            var ends = times.ToInstant(timeZone, evt.EndsLocal);
            if (starts.Instant is not { } startsAt || ends.Instant is not { } endsAt || endsAt <= startsAt)
            {
                return VenueProblems.InvalidVenue($"Event {evt.PublicId} has a start or end time that does not exist in {timeZone}.");
            }

            if (startsAt == evt.StartsAt && endsAt == evt.EndsAt)
            {
                continue;
            }

            evt.StartsAt = startsAt;
            evt.EndsAt = endsAt;
            evt.UpdatedAt = now;
            if (evt.Status != EventStatus.Draft)
            {
                transitions.PurgeAfterCommit(evt);
            }
        }

        return null;
    }

    private async Task<Venue?> LockVenueAsync(string publicId, CancellationToken cancellationToken) =>
        await db.Venues.FromSql($"SELECT * FROM venues WHERE public_id = {publicId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);

    private async Task<List<HockeyEvent>> LockEventsAsync(Guid venueId, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var events = await db.Events
            .FromSql($"SELECT e.*, e.xmin FROM events e WHERE e.venue_id = {venueId} ORDER BY e.id FOR UPDATE")
            .ToListAsync(cancellationToken);
        return includeArchived ? events : [.. events.Where(evt => evt.Status != EventStatus.Archived)];
    }
}
