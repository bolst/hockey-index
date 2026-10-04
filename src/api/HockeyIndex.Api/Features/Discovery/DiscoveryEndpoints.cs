using HockeyIndex.Api.Infrastructure.Caching;
using HockeyIndex.Api.Infrastructure.RateLimiting;

namespace HockeyIndex.Api.Features.Discovery;

/// <summary>Anonymous discovery surface. Paths are in <c>PublicPaths</c>: no auth pipeline, no cookies, ACAO *.</summary>
public static class DiscoveryEndpoints
{
    public const int SearchSharedMaxAgeSeconds = 60;
    public const int SearchStaleWhileRevalidateSeconds = 30;
    public const int EventSharedMaxAgeSeconds = 60;
    public const int SitemapSharedMaxAgeSeconds = 3600;

    public static IServiceCollection AddDiscoveryFeature(this IServiceCollection services)
    {
        services.AddScoped<SearchEvents>();
        services.AddScoped<GetPublicEvent>();
        services.AddScoped<Sitemap>();
        return services;
    }

    public static void Map(RouteGroupBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);

        v1.MapGet("/search", (HttpRequest request, HttpResponse response, SearchEvents search, CancellationToken ct) =>
                search.HandleAsync(request, response, ct))
            .CachePublic(SearchSharedMaxAgeSeconds, SearchStaleWhileRevalidateSeconds)
            .RequireRateLimiting(RateLimitPolicies.PublicRead);
        v1.MapGet("/events/{publicId}", (string publicId, HttpResponse response, GetPublicEvent get, CancellationToken ct) =>
                get.HandleAsync(publicId, response, ct))
            .CachePublic(EventSharedMaxAgeSeconds)
            .RequireRateLimiting(RateLimitPolicies.PublicRead);
        v1.MapGet("/geo/ip", (HttpRequest request) => TypedResults.Ok(GetIpLocation.Handle(request)))
            .RequireRateLimiting(RateLimitPolicies.PublicRead);
        v1.MapGet("/sitemap.xml", (Sitemap sitemap, CancellationToken ct) => sitemap.HandleAsync(ct))
            .CachePublic(SitemapSharedMaxAgeSeconds)
            .RequireRateLimiting(RateLimitPolicies.PublicRead);
    }
}
