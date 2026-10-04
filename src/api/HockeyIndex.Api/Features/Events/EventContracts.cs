using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Time;
using Microsoft.AspNetCore.Http.HttpResults;
using NpgsqlTypes;

namespace HockeyIndex.Api.Features.Events;

/// <summary>Inclusive skill bounds, 0–7.</summary>
public sealed record SkillRangeDto(int Min, int Max)
{
    public static SkillRangeDto From(NpgsqlRange<int> range) => new(
        range.LowerBoundIsInclusive ? range.LowerBound : range.LowerBound + 1,
        range.UpperBoundIsInclusive ? range.UpperBound : range.UpperBound - 1);

    public NpgsqlRange<int> ToRange() => new(Min, true, Max, true);
}

/// <summary>Body of POST /v1/host/events and PUT /v1/host/events/{id}.</summary>
/// <param name="VenueId">Venue public id from /v1/venues.</param>
/// <param name="Type"><c>scrimmage</c>, <c>league</c> or <c>tournament</c>.</param>
/// <param name="StartsLocal">Scrimmage only: venue wall time without offset, e.g. <c>2026-10-10T19:00:00</c>.</param>
/// <param name="EndsLocal">Scrimmage only: venue wall time without offset.</param>
/// <param name="StartDate">League/tournament only: first day of the season.</param>
/// <param name="EndDate">League/tournament only: last day of the season (inclusive).</param>
/// <param name="ScheduleText">League/tournament only: free-text schedule.</param>
/// <param name="FeeCents">Null means "see Join Instructions".</param>
/// <param name="Currency"><c>CAD</c> or <c>USD</c>; defaults from the venue country.</param>
/// <param name="JoinInstructions">
/// On a published or cancelled event, send the text the host wants live (the pending text when one exists).
/// A changed value is link-scanned before it goes live.
/// </param>
public sealed record EventRequest(
    string? VenueId,
    string? RinkLabel,
    string? Type,
    string? Title,
    string? Description,
    DateTime? StartsLocal,
    DateTime? EndsLocal,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? ScheduleText,
    SkillRangeDto? Skill,
    int? FeeCents,
    string? Currency,
    string? JoinInstructions);

public sealed record EventVenueSummary(string PublicId, string Name, string AddressLine, string City, string Region, string Country, string TimeZone)
{
    public static EventVenueSummary From(Venue venue)
    {
        ArgumentNullException.ThrowIfNull(venue);
        return new(venue.PublicId, venue.Name, venue.AddressLine, venue.City, venue.Region, venue.Country, venue.TimeZone);
    }
}

/// <param name="StartsAt">Start instant rendered in the venue's offset at that instant.</param>
/// <param name="StartDate">League/tournament: first season day; null for scrimmages.</param>
/// <param name="EndDate">League/tournament: last season day (inclusive); null for scrimmages.</param>
/// <param name="PendingJoinInstructions">An edit waiting for a link scan; <see cref="JoinInstructions"/> stays live meanwhile.</param>
/// <param name="PublishRequestedAt">Set while a publish waits for the link scanner.</param>
/// <param name="Notice">Why a parked request ended without being applied.</param>
/// <param name="Version">Same value as the <c>ETag</c> header (unquoted).</param>
/// <param name="ResolvedAmbiguousTimes">
/// Fields (<c>startsLocal</c>, <c>endsLocal</c>) whose wall time occurs twice at a DST change; the earlier instant was used.
/// </param>
public sealed record EventResponse(
    string PublicId,
    string Status,
    string Type,
    string Title,
    string? Description,
    EventVenueSummary Venue,
    string? RinkLabel,
    DateTime StartsLocal,
    DateTime EndsLocal,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? ScheduleText,
    SkillRangeDto Skill,
    int? FeeCents,
    string Currency,
    string JoinInstructions,
    string? PendingJoinInstructions,
    DateTimeOffset? PublishRequestedAt,
    string? HiddenReason,
    string? Notice,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    IReadOnlyList<string> ResolvedAmbiguousTimes)
{
    public static EventResponse From(HockeyEvent evt, Venue venue, VenueTimeConverter times, IReadOnlyList<string>? resolvedAmbiguousTimes = null)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(venue);
        ArgumentNullException.ThrowIfNull(times);

        var isSeason = evt.Type != EventType.Scrimmage;
        return new(
            evt.PublicId,
            SnakeCaseText.Of(evt.Status),
            SnakeCaseText.Of(evt.Type),
            evt.Title,
            evt.Description,
            EventVenueSummary.From(venue),
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
            evt.PendingJoinInstructions,
            evt.PublishRequestedAt,
            evt.HiddenReason is { } reason ? SnakeCaseText.Of(reason) : null,
            evt.Notice is { } notice ? SnakeCaseText.Of(notice) : null,
            evt.PublishedAt,
            evt.CancelledAt,
            evt.ArchivedAt,
            evt.CreatedAt,
            evt.UpdatedAt,
            evt.Version,
            resolvedAmbiguousTimes ?? []);
    }
}

/// <param name="ActiveListings">Published or cancelled events that have not ended.</param>
/// <param name="PublishesLast24Hours">Draft-to-published transitions in the last 24 hours.</param>
public sealed record HostEventLimits(int ActiveListings, int MaxActiveListings, int PublishesLast24Hours, int MaxPublishesPer24Hours);

public sealed record MyEventsResponse(IReadOnlyList<EventResponse> Events, HostEventLimits Limits);

internal static class EventProblems
{
    public static IResult NotFound() => Of(StatusCodes.Status404NotFound, "event_not_found", "Event not found.");
    public static IResult VenueNotFound() => Of(StatusCodes.Status422UnprocessableEntity, "venue_not_found", "Venue not found.");
    public static IResult Ended() => Of(StatusCodes.Status422UnprocessableEntity, "event_ended", "This event has already ended.");
    public static IResult IfMatchRequired() => Of(StatusCodes.Status428PreconditionRequired, "if_match_required", "Send the event's ETag in If-Match.");
    public static IResult Stale() => Of(StatusCodes.Status412PreconditionFailed, "event_stale", "The event changed since you loaded it. Reload and retry.");
    public static IResult Changed() => Of(StatusCodes.Status409Conflict, "event_changed", "The event changed while its links were checked. Reload and retry.");
    public static IResult NotDraft() => Of(StatusCodes.Status409Conflict, "event_not_draft", "Only drafts can do this.");
    public static IResult NotEditable() => Of(StatusCodes.Status409Conflict, "event_not_editable", "This event can no longer be edited.");
    public static IResult InvalidTransition() => Of(StatusCodes.Status409Conflict, "invalid_transition", "This event cannot change to that state.");
    public static IResult HostInactive() => Of(StatusCodes.Status403Forbidden, "host_inactive", "Your account cannot publish events.");

    public static IResult ActiveLimitReached() => Of(
        StatusCodes.Status429TooManyRequests,
        "active_limit_reached",
        $"You can have at most {PublishEvent.MaxActiveListings} active listings.");

    public static IResult DailyLimitReached() => Of(
        StatusCodes.Status429TooManyRequests,
        "daily_publish_limit_reached",
        $"You can publish at most {PublishEvent.MaxPublishesPer24Hours} events per day.");

    public static IResult Invalid(IReadOnlyDictionary<string, string> errors) =>
        TypedResults.Problem(
            title: "Check the highlighted fields.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?> { ["code"] = "invalid_event", ["errors"] = errors });

    public static IResult LocalTimeSkipped(string field) =>
        TypedResults.Problem(
            title: "That local time does not exist at the venue (daylight saving change).",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?> { ["code"] = "local_time_skipped", ["field"] = field });

    public static IResult Blocked(IReadOnlyList<string> blockedUrls, bool tooManyUrls) =>
        TypedResults.Problem(
            title: "Join instructions contain links that are not allowed.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "join_instructions_blocked",
                ["blockedUrls"] = blockedUrls,
                ["tooManyUrls"] = tooManyUrls,
            });

    private static ProblemHttpResult Of(int status, string code, string title) =>
        TypedResults.Problem(title: title, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
