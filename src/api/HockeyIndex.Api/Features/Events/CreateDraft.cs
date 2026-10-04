using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using NodaTime;

namespace HockeyIndex.Api.Features.Events;

public sealed class CreateDraft(AppDbContext db, EventValidator validator, EventResponder responder, IClock clock)
{
    public async Task<IResult> HandleAsync(EventRequest request, Guid hostId, HttpResponse response, CancellationToken cancellationToken)
    {
        var (validated, problem) = await validator.ValidateAsync(request, cancellationToken);
        if (validated is null)
        {
            return problem!;
        }

        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        var evt = new HockeyEvent
        {
            PublicId = PublicId.New(),
            HostId = hostId,
            VenueId = validated.Venue.Id,
            Type = validated.Type,
            Title = validated.Title,
            StartsLocal = validated.StartsLocal,
            EndsLocal = validated.EndsLocal,
            StartsAt = validated.StartsAt,
            EndsAt = validated.EndsAt,
            SkillRange = validated.SkillRange,
            Currency = validated.Currency,
            JoinInstructions = validated.JoinInstructions,
            CreatedAt = now,
            UpdatedAt = now,
        };
        validated.ApplyTo(evt, now);
        db.Events.Add(evt);
        await db.SaveChangesAsync(cancellationToken);

        var body = await responder.ToResponseAsync(evt, response, cancellationToken, validated.ResolvedAmbiguousTimes);
        return TypedResults.Created($"/v1/host/events/{evt.PublicId}", body);
    }
}
