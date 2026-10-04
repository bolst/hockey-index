using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Features.Events;

/// <summary>The host's events, newest first, with cap usage and pending-scan state.</summary>
public sealed class ListMyEvents(AppDbContext db, VenueTimeConverter times, IClock clock)
{
    public const int MaxEvents = 200;

    public async Task<IResult> HandleAsync(string? status, Guid hostId, CancellationToken cancellationToken)
    {
        EventStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!SnakeCaseText.TryParse<EventStatus>(status.Trim(), out var parsed))
            {
                return EventProblems.Invalid(new Dictionary<string, string> { ["status"] = "Unknown status." });
            }

            statusFilter = parsed;
        }

        var query = db.Events.AsNoTracking().Where(evt => evt.HostId == hostId);
        if (statusFilter is { } filter)
        {
            query = query.Where(evt => evt.Status == filter);
        }

        var rows = await query
            .OrderByDescending(evt => evt.CreatedAt)
            .ThenByDescending(evt => evt.Id)
            .Take(MaxEvents)
            .Join(db.Venues.AsNoTracking(), evt => evt.VenueId, venue => venue.Id, (evt, venue) => new { evt, venue })
            .ToListAsync(cancellationToken);

        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        var limits = new HostEventLimits(
            await PublishEvent.CountActiveListingsAsync(db, hostId, now, cancellationToken),
            PublishEvent.MaxActiveListings,
            await PublishEvent.CountPublishesLast24HoursAsync(db, hostId, now, cancellationToken),
            PublishEvent.MaxPublishesPer24Hours);

        return TypedResults.Ok(new MyEventsResponse([.. rows.Select(row => EventResponse.From(row.evt, row.venue, times))], limits));
    }
}
