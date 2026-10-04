using HockeyIndex.Api.Features.Events;

namespace HockeyIndex.Api.Features.Discovery;

public sealed record PublicVenue(
    string PublicId,
    string Name,
    string AddressLine,
    string City,
    string Region,
    string Country,
    string TimeZone,
    double Latitude,
    double Longitude);

/// <param name="StartsAt">Start instant in the venue's offset at that instant.</param>
/// <param name="StartDate">League/tournament: first season day; null for scrimmages.</param>
/// <param name="EndDate">League/tournament: last season day (inclusive); null for scrimmages.</param>
/// <param name="FeeCents">Null means "see Join Instructions".</param>
/// <param name="DistanceMiles">From the search point, one decimal.</param>
public sealed record SearchResult(
    string PublicId,
    string Status,
    string Type,
    string Title,
    PublicVenue Venue,
    string? RinkLabel,
    DateTime StartsLocal,
    DateTime EndsLocal,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly? StartDate,
    DateOnly? EndDate,
    SkillRangeDto Skill,
    int? FeeCents,
    string Currency,
    double DistanceMiles);

/// <param name="Truncated">More than <see cref="SearchEvents.MaxResults"/> events matched; narrow the filters.</param>
public sealed record SearchResponse(IReadOnlyList<SearchResult> Events, bool Truncated);

/// <summary>An event as anyone may see it. Only clean <see cref="JoinInstructions"/> are ever returned.</summary>
/// <param name="Status"><c>published</c>, <c>cancelled</c>, or <c>archived</c> (read-only, not indexed).</param>
public sealed record PublicEventResponse(
    string PublicId,
    string Status,
    string Type,
    string Title,
    string? Description,
    PublicVenue Venue,
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
    DateTimeOffset? PublishedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset UpdatedAt);

/// <summary>Approximate visitor location from Cloudflare, rounded to the 2 decimals search accepts. Nulls when unknown.</summary>
public sealed record IpLocationResponse(decimal? Latitude, decimal? Longitude);

internal static class DiscoveryProblems
{
    public static IResult EventNotFound() => TypedResults.Problem(
        title: "Event not found.",
        statusCode: StatusCodes.Status404NotFound,
        extensions: new Dictionary<string, object?> { ["code"] = "event_not_found" });

    public static IResult InvalidSearch(IReadOnlyDictionary<string, string> errors) => TypedResults.Problem(
        title: "Check the search parameters.",
        statusCode: StatusCodes.Status400BadRequest,
        extensions: new Dictionary<string, object?> { ["code"] = "invalid_search", ["errors"] = errors });
}
