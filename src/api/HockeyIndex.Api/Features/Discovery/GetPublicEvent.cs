using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace HockeyIndex.Api.Features.Discovery;

/// <summary>
/// GET /v1/events/{publicId}. Visible: published, cancelled, or archived without a hidden reason (read-only, <c>noindex</c>).
/// Everything else is a 404, which is edge-cached under the same <c>Cache-Tag</c> so a purge also clears it.
/// </summary>
public sealed class GetPublicEvent(AppDbContext db, VenueTimeConverter times)
{
    public const string RobotsTagHeader = "X-Robots-Tag";

    public static string CacheTagFor(string publicId) => $"event-{publicId}";

    public async Task<IResult> HandleAsync(string publicId, HttpResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!PublicId.IsValid(publicId))
        {
            return DiscoveryProblems.EventNotFound();
        }

        response.Headers["Cache-Tag"] = CacheTagFor(publicId);
        var row = await (
                from evt in db.Events.AsNoTracking()
                join venue in db.Venues.AsNoTracking() on evt.VenueId equals venue.Id
                where evt.PublicId == publicId
                    && (evt.Status == EventStatus.Published
                        || evt.Status == EventStatus.Cancelled
                        || (evt.Status == EventStatus.Archived && evt.HiddenReason == null))
                select new { evt, venue })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return DiscoveryProblems.EventNotFound();
        }

        if (row.evt.Status == EventStatus.Archived)
        {
            response.Headers[RobotsTagHeader] = "noindex";
        }

        return TypedResults.Ok(ToResponse(row.evt, row.venue));
    }

    private PublicEventResponse ToResponse(HockeyEvent evt, Venue venue)
    {
        var isSeason = evt.Type != EventType.Scrimmage;
        return new PublicEventResponse(
            evt.PublicId,
            SnakeCaseText.Of(evt.Status),
            SnakeCaseText.Of(evt.Type),
            evt.Title,
            evt.Description,
            new PublicVenue(
                venue.PublicId, venue.Name, venue.AddressLine, venue.City, venue.Region, venue.Country, venue.TimeZone, venue.Geo.Y, venue.Geo.X),
            evt.RinkLabel,
            evt.StartsLocal,
            evt.EndsLocal,
            times.ToVenueOffset(venue.TimeZone, evt.StartsAt),
            times.ToVenueOffset(venue.TimeZone, evt.EndsAt),
            isSeason ? DateOnly.FromDateTime(evt.StartsLocal) : null,
            isSeason ? DateOnly.FromDateTime(evt.EndsLocal).AddDays(-1) : null,
            evt.ScheduleText,
            SkillRangeDto.From(evt.SkillRange),
            evt.FeeCents,
            evt.Currency,
            evt.JoinInstructions,
            evt.PublishedAt,
            evt.CancelledAt,
            evt.UpdatedAt);
    }
}
