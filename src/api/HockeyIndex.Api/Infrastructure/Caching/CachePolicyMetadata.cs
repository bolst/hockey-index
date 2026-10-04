using System.Globalization;

namespace HockeyIndex.Api.Infrastructure.Caching;

public sealed record CachePublicMetadata(int SharedMaxAgeSeconds, int? StaleWhileRevalidateSeconds)
{
    public string ToCacheControl() => StaleWhileRevalidateSeconds is { } swr
        ? string.Create(CultureInfo.InvariantCulture, $"public, max-age=0, s-maxage={SharedMaxAgeSeconds}, stale-while-revalidate={swr}")
        : string.Create(CultureInfo.InvariantCulture, $"public, max-age=0, s-maxage={SharedMaxAgeSeconds}");
}

public static class CachePolicyEndpointExtensions
{
    /// <summary>Opts an endpoint on a public path into edge caching; every other response is private, no-store.</summary>
    public static TBuilder CachePublic<TBuilder>(this TBuilder builder, int sMaxAge, int? staleWhileRevalidate = null)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new CachePublicMetadata(sMaxAge, staleWhileRevalidate));
}
