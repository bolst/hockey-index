using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Venues;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NpgsqlTypes;

namespace HockeyIndex.Api.Features.Events;

/// <summary>A request that passed <see cref="EventValidator"/>, with times resolved in the venue zone.</summary>
public sealed record ValidatedEvent(
    Venue Venue,
    string? RinkLabel,
    EventType Type,
    string Title,
    string? Description,
    DateTime StartsLocal,
    DateTime EndsLocal,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? ScheduleText,
    NpgsqlRange<int> SkillRange,
    int? FeeCents,
    string Currency,
    string JoinInstructions,
    IReadOnlyList<string> ResolvedAmbiguousTimes)
{
    /// <summary>Copies every field except join instructions, which the caller writes according to the scan result.</summary>
    public void ApplyTo(HockeyEvent evt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(evt);
        evt.VenueId = Venue.Id;
        evt.RinkLabel = RinkLabel;
        evt.Type = Type;
        evt.Title = Title;
        evt.Description = Description;
        evt.StartsLocal = StartsLocal;
        evt.EndsLocal = EndsLocal;
        evt.StartsAt = StartsAt;
        evt.EndsAt = EndsAt;
        evt.ScheduleText = ScheduleText;
        evt.SkillRange = SkillRange;
        evt.FeeCents = FeeCents;
        evt.Currency = Currency;
        evt.UpdatedAt = now;
    }
}

/// <summary>Input rules for events (AC-LC-1, AC-TS-7): type-dependent times, length limits, skill 0–7, fee bounds.</summary>
public sealed class EventValidator(AppDbContext db, VenueTimeConverter times, IClock clock)
{
    public const string StartsLocalField = "startsLocal";
    public const string EndsLocalField = "endsLocal";

    /// <returns>The validated event, or a 422 problem.</returns>
    public async Task<(ValidatedEvent? Event, IResult? Problem)> ValidateAsync(EventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var type = SnakeCaseText.TryParse<EventType>(request.Type?.Trim(), out var parsedType) ? parsedType : (EventType?)null;
        if (type is null)
        {
            errors["type"] = "Choose scrimmage, league or tournament.";
        }

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is 0 or > HockeyEvent.TitleMaxLength)
        {
            errors["title"] = $"Title must be 1–{HockeyEvent.TitleMaxLength} characters.";
        }

        var description = NullIfBlank(request.Description);
        if (description?.Length > HockeyEvent.DescriptionMaxLength)
        {
            errors["description"] = $"Description must be at most {HockeyEvent.DescriptionMaxLength} characters.";
        }

        var rinkLabel = NullIfBlank(request.RinkLabel);
        if (rinkLabel?.Length > HockeyEvent.RinkLabelMaxLength)
        {
            errors["rinkLabel"] = $"Rink label must be at most {HockeyEvent.RinkLabelMaxLength} characters.";
        }

        var scheduleText = NullIfBlank(request.ScheduleText);
        if (scheduleText?.Length > HockeyEvent.ScheduleTextMaxLength)
        {
            errors["scheduleText"] = $"Schedule must be at most {HockeyEvent.ScheduleTextMaxLength} characters.";
        }

        if (request.Skill is not { } skill
            || skill.Min < HockeyEvent.MinSkill || skill.Max > HockeyEvent.MaxSkill || skill.Min > skill.Max)
        {
            errors["skill"] = $"Skill must be a range within {HockeyEvent.MinSkill}–{HockeyEvent.MaxSkill} with min ≤ max.";
        }

        if (request.FeeCents is < 0 or > HockeyEvent.MaxFeeCents)
        {
            errors["feeCents"] = $"Fee must be between 0 and {HockeyEvent.MaxFeeCents} cents, or empty.";
        }

        var currency = NullIfBlank(request.Currency)?.ToUpperInvariant();
        if (currency is not null && !HockeyEvent.Currencies.Contains(currency))
        {
            errors["currency"] = "Currency must be CAD or USD.";
        }

        var joinInstructions = request.JoinInstructions?.Trim() ?? string.Empty;
        if (joinInstructions.Length is 0 or > HockeyEvent.JoinInstructionsMaxLength)
        {
            errors["joinInstructions"] = $"Join instructions must be 1–{HockeyEvent.JoinInstructionsMaxLength} characters.";
        }

        var localTimes = type is { } knownType ? LocalTimes(request, knownType, scheduleText, errors) : null;

        if (errors.Count > 0)
        {
            return (null, EventProblems.Invalid(errors));
        }

        if (await FindVenueAsync(request.VenueId, cancellationToken) is not { } venue)
        {
            return (null, EventProblems.VenueNotFound());
        }

        var (startsLocal, endsLocal) = localTimes!.Value;
        var starts = times.ToInstant(venue.TimeZone, startsLocal);
        if (starts.WasSkipped)
        {
            return (null, EventProblems.LocalTimeSkipped(StartsLocalField));
        }

        var ends = times.ToInstant(venue.TimeZone, endsLocal);
        if (ends.WasSkipped)
        {
            return (null, EventProblems.LocalTimeSkipped(EndsLocalField));
        }

        if (ends.Instant <= starts.Instant)
        {
            return (null, EventProblems.Invalid(new Dictionary<string, string> { [EndsLocalField] = "End must be after start." }));
        }

        if (ends.Instant <= clock.GetCurrentInstant().ToDateTimeOffset())
        {
            return (null, EventProblems.Invalid(new Dictionary<string, string> { [EndsLocalField] = "End must be in the future." }));
        }

        List<string> ambiguous = [];
        if (starts.WasAmbiguous)
        {
            ambiguous.Add(StartsLocalField);
        }

        if (ends.WasAmbiguous)
        {
            ambiguous.Add(EndsLocalField);
        }

        return (new ValidatedEvent(
            venue,
            rinkLabel,
            type!.Value,
            title,
            description,
            startsLocal,
            endsLocal,
            starts.Instant!.Value,
            ends.Instant!.Value,
            scheduleText,
            request.Skill!.ToRange(),
            request.FeeCents,
            currency ?? DefaultCurrency(venue.Country),
            joinInstructions,
            ambiguous), null);
    }

    public static string DefaultCurrency(string country) => country == "CA" ? "CAD" : "USD";

    private static (DateTime StartsLocal, DateTime EndsLocal)? LocalTimes(
        EventRequest request, EventType type, string? scheduleText, Dictionary<string, string> errors)
    {
        if (type == EventType.Scrimmage)
        {
            if (request.StartDate is not null || request.EndDate is not null)
            {
                errors["startDate"] = "Scrimmages use startsLocal and endsLocal, not dates.";
            }

            if (scheduleText is not null)
            {
                errors["scheduleText"] = "Only leagues and tournaments have a schedule.";
            }

            if (request.StartsLocal is not { Kind: DateTimeKind.Unspecified } startsLocal)
            {
                errors[StartsLocalField] = "Start must be a venue-local date and time without an offset.";
                return null;
            }

            if (request.EndsLocal is not { Kind: DateTimeKind.Unspecified } endsLocal)
            {
                errors[EndsLocalField] = "End must be a venue-local date and time without an offset.";
                return null;
            }

            if (endsLocal <= startsLocal)
            {
                errors[EndsLocalField] = "End must be after start.";
                return null;
            }

            return (TruncateToMinute(startsLocal), TruncateToMinute(endsLocal));
        }

        if (request.StartsLocal is not null || request.EndsLocal is not null)
        {
            errors[StartsLocalField] = "Leagues and tournaments use startDate and endDate, not times.";
        }

        if (request.StartDate is not { } startDate)
        {
            errors["startDate"] = "Start date is required.";
            return null;
        }

        if (request.EndDate is not { } endDate || endDate < startDate)
        {
            errors["endDate"] = "End date must be on or after the start date.";
            return null;
        }

        return (startDate.ToDateTime(TimeOnly.MinValue), endDate.AddDays(1).ToDateTime(TimeOnly.MinValue));
    }

    private async Task<Venue?> FindVenueAsync(string? venuePublicId, CancellationToken cancellationToken)
    {
        if (!PublicId.IsValid(venuePublicId))
        {
            return null;
        }

        var venue = await db.Venues.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.PublicId == venuePublicId, cancellationToken);
        if (venue is null)
        {
            return null;
        }

        var current = await VenueMerges.FollowAsync(db, venue, cancellationToken);
        return times.IsKnownZone(current.TimeZone) ? current : null;
    }

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Unspecified);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
