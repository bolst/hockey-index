using HockeyIndex.Api.Features.Admin;
using HockeyIndex.Api.Features.Auth;
using HockeyIndex.Api.Features.Discovery;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Features.Venues;

namespace HockeyIndex.Api.Features;

/// <summary>
/// Central registration for feature endpoints. Each feature exposes <c>static void Map(RouteGroupBuilder v1)</c>
/// and is added here, e.g. <c>SearchEvents.Map(v1)</c>. Public endpoints add <c>.CachePublic(...)</c> and
/// <c>.RequireRateLimiting(RateLimitPolicies.PublicRead)</c>.
/// </summary>
public static class FeatureEndpoints
{
    public static IEndpointRouteBuilder MapFeatureEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var v1 = app.MapGroup("/v1");
        AuthEndpoints.Map(v1);
        VenueEndpoints.Map(v1);
        EventEndpoints.Map(v1);
        DiscoveryEndpoints.Map(v1);
        SafetyEndpoints.Map(v1);
        AdminEndpoints.Map(v1);
        return app;
    }
}
