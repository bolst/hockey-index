namespace HockeyIndex.Api.Infrastructure.Http;

/// <summary>Anonymous, edge-cacheable surface: no auth pipeline, no cookies, ACAO *.</summary>
public static class PublicPaths
{
    private static readonly PathString[] Prefixes =
    [
        "/v1/search",
        "/v1/events",
        "/v1/geo/ip",
        "/v1/sitemap.xml",
        "/v1/out/check",
        "/healthz",
    ];

    public static bool Match(HttpContext context) => Match(context.Request.Path);

    public static bool Match(PathString path) =>
        Array.Exists(Prefixes, prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Paths the SPA calls with credentials (cookie); CORS echoes the exact SPA origin.</summary>
public static class CredentialedPaths
{
    private static readonly PathString[] Prefixes = ["/v1/auth", "/v1/me", "/v1/host", "/v1/admin", "/v1/venues"];

    public static bool Match(PathString path) =>
        Array.Exists(Prefixes, prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
