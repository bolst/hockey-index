using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Integrations.Geocoding;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HockeyIndex.Api.Features.Venues;

/// <param name="Name">Facility name typed by the host (Mapbox v6 returns addresses, not facilities).</param>
/// <param name="ProviderPlaceId">The selected suggestion's <c>providerPlaceId</c>.</param>
/// <param name="Address">The selected suggestion's <c>label</c> (full address).</param>
/// <param name="Latitude">The selected suggestion's latitude; the permanent lookup must land within 50 m.</param>
/// <param name="ConfirmNew">Create even when "Did you mean…?" candidates exist.</param>
public sealed record CreateVenueRequest(
    string? Name, string? ProviderPlaceId, string? Address, double? Latitude, double? Longitude, bool ConfirmNew = false);

/// <param name="DistanceMeters">Distance from the search point or new venue; <c>null</c> when not applicable.</param>
public sealed record VenueResponse(
    string PublicId,
    string Name,
    string AddressLine,
    string City,
    string Region,
    string Country,
    double Latitude,
    double Longitude,
    string TimeZone,
    double? DistanceMeters = null)
{
    public static VenueResponse From(Venue venue, double? distanceMeters = null)
    {
        ArgumentNullException.ThrowIfNull(venue);
        return new(
            venue.PublicId, venue.Name, venue.AddressLine, venue.City, venue.Region, venue.Country,
            venue.Geo.Y, venue.Geo.X, venue.TimeZone, distanceMeters);
    }
}

/// <summary>A temporary provider result. Display only: the client MUST NOT persist it; POST /v1/venues turns it into a venue.</summary>
public sealed record AddressSuggestion(
    string ProviderPlaceId, string Label, string AddressLine, string City, string Region, string Country, double Latitude, double Longitude)
{
    public static AddressSuggestion From(GeocodedAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return new(
            address.ProviderPlaceId, address.FullAddress, address.AddressLine, address.City, address.Region, address.Country,
            address.Latitude, address.Longitude);
    }
}

public static class SuggestionsStatus
{
    /// <summary>The provider was queried; <c>suggestions</c> holds its results (possibly none).</summary>
    public const string Ok = "ok";

    /// <summary>The database returned enough venues; the provider was not queried.</summary>
    public const string NotNeeded = "not_needed";

    /// <summary>Breaker open or provider error: "new venues temporarily unavailable".</summary>
    public const string Unavailable = "unavailable";

    /// <summary>The host's or the global daily lookup budget is spent.</summary>
    public const string LimitReached = "limit_reached";
}

public sealed record AutocompleteVenuesResponse(
    IReadOnlyList<VenueResponse> Venues, IReadOnlyList<AddressSuggestion> Suggestions, string SuggestionsStatus);

internal static class VenueProblems
{
    public static IResult InvalidQuery() => Of(StatusCodes.Status400BadRequest, "invalid_query", "Enter at least 2 characters.");
    public static IResult InvalidVenue(string detail) => Of(StatusCodes.Status422UnprocessableEntity, "invalid_venue", detail);
    public static IResult AddressNotFound() => Of(StatusCodes.Status422UnprocessableEntity, "address_not_found", "The selected address could not be confirmed. Search again.");
    public static IResult LocationMismatch() => Of(StatusCodes.Status422UnprocessableEntity, "location_mismatch", "The selected address moved when confirmed. Search again.");
    public static IResult TimeZoneUnknown() => Of(StatusCodes.Status422UnprocessableEntity, "time_zone_unknown", "No time zone is known for this location.");
    public static IResult NotFound() => Of(StatusCodes.Status404NotFound, "venue_not_found", "Venue not found.");
    public static IResult LimitReached() => Of(StatusCodes.Status429TooManyRequests, "venue_limit_reached", "You have added too many venues today. Try again tomorrow.");
    public static IResult ProviderUnavailable() => Of(StatusCodes.Status503ServiceUnavailable, "venue_provider_unavailable", "New venues are temporarily unavailable.");

    public static IResult Candidates(IReadOnlyList<VenueResponse> candidates) =>
        TypedResults.Problem(
            title: "Did you mean one of these venues?",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = "venue_candidates", ["candidates"] = candidates });

    private static ProblemHttpResult Of(int status, string code, string title) =>
        TypedResults.Problem(title: title, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
