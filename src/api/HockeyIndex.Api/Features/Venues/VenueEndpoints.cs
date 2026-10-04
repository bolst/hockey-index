using System.Security.Claims;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.RateLimiting;

namespace HockeyIndex.Api.Features.Venues;

public static class VenueEndpoints
{
    public static IServiceCollection AddVenuesFeature(this IServiceCollection services)
    {
        services.AddOptions<VenueOptions>().BindConfiguration(VenueOptions.SectionName);
        services.AddScoped<MapboxAccess>();
        services.AddScoped<VenueDeduplicator>();
        services.AddScoped<AutocompleteVenues>();
        services.AddScoped<CreateVenue>();
        services.AddScoped<GetVenue>();
        return services;
    }

    public static void Map(RouteGroupBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);

        var venues = v1.MapGroup("/venues").RequireAuthorization();
        venues.MapGet("/autocomplete", (string? q, double? lat, double? lng, ClaimsPrincipal user, AutocompleteVenues autocomplete, CancellationToken ct) =>
                user.GetHostId() is { } hostId ? autocomplete.HandleAsync(q, lat, lng, hostId, ct) : Task.FromResult<IResult>(TypedResults.Unauthorized()))
            .RequireRateLimiting(RateLimitPolicies.VenueLookup);
        venues.MapPost("", (CreateVenueRequest request, ClaimsPrincipal user, CreateVenue create, CancellationToken ct) =>
                user.GetHostId() is { } hostId ? create.HandleAsync(request, hostId, ct) : Task.FromResult<IResult>(TypedResults.Unauthorized()))
            .RequireRateLimiting(RateLimitPolicies.VenueLookup);
        venues.MapGet("/{publicId}", (string publicId, GetVenue get, CancellationToken ct) => get.HandleAsync(publicId, ct))
            .RequireRateLimiting(RateLimitPolicies.HostWrite);
    }
}
