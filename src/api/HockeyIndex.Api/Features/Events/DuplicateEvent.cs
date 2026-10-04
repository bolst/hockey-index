using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Features.Events;

/// <summary>
/// Copies an event into a new draft one week later (AC-LC-8): venue wall times +7 days, UTC recomputed in the venue zone
/// so the local start survives DST changes. Copies the live join instructions, never the pending text or a publish request.
/// </summary>
public sealed class DuplicateEvent(AppDbContext db, VenueTimeConverter times, EventResponder responder, IClock clock)
{
    public static readonly TimeSpan Offset = TimeSpan.FromDays(7);

    public async Task<IResult> HandleAsync(string publicId, Guid hostId, HttpResponse response, CancellationToken cancellationToken)
    {
        if (await EventRows.FindOwnedAsync(db, publicId, hostId, cancellationToken) is not { } source)
        {
            return EventProblems.NotFound();
        }

        var timeZone = await db.Venues.AsNoTracking()
            .Where(venue => venue.Id == source.VenueId)
            .Select(venue => venue.TimeZone)
            .SingleAsync(cancellationToken);

        var startsLocal = source.StartsLocal + Offset;
        var endsLocal = source.EndsLocal + Offset;
        var starts = times.ToInstant(timeZone, startsLocal);
        if (starts.WasSkipped)
        {
            return EventProblems.LocalTimeSkipped(EventValidator.StartsLocalField);
        }

        var ends = times.ToInstant(timeZone, endsLocal);
        if (ends.WasSkipped)
        {
            return EventProblems.LocalTimeSkipped(EventValidator.EndsLocalField);
        }

        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        var copy = new HockeyEvent
        {
            PublicId = PublicId.New(),
            HostId = hostId,
            VenueId = source.VenueId,
            RinkLabel = source.RinkLabel,
            Type = source.Type,
            Title = source.Title,
            Description = source.Description,
            StartsLocal = startsLocal,
            EndsLocal = endsLocal,
            StartsAt = starts.Instant!.Value,
            EndsAt = ends.Instant!.Value,
            ScheduleText = source.ScheduleText,
            SkillRange = source.SkillRange,
            FeeCents = source.FeeCents,
            Currency = source.Currency,
            JoinInstructions = source.JoinInstructions,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Events.Add(copy);
        await db.SaveChangesAsync(cancellationToken);

        List<string> ambiguous = [];
        if (starts.WasAmbiguous)
        {
            ambiguous.Add(EventValidator.StartsLocalField);
        }

        if (ends.WasAmbiguous)
        {
            ambiguous.Add(EventValidator.EndsLocalField);
        }

        var body = await responder.ToResponseAsync(copy, response, cancellationToken, ambiguous);
        return TypedResults.Created($"/v1/host/events/{copy.PublicId}", body);
    }
}
