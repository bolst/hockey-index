using System.Globalization;

namespace HockeyIndex.Api.Features.Discovery;

/// <summary>
/// GET /v1/geo/ip: fallback when the browser denies geolocation (AC-DS-1). Reads the headers added by Cloudflare's
/// "visitor location headers" managed transform. Per-visitor, so the response stays <c>private, no-store</c>.
/// </summary>
public static class GetIpLocation
{
    public const string LatitudeHeader = "cf-iplatitude";
    public const string LongitudeHeader = "cf-iplongitude";

    public static IpLocationResponse Handle(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var latitude = Read(request, LatitudeHeader, 90);
        var longitude = Read(request, LongitudeHeader, 180);
        return latitude is null || longitude is null ? new IpLocationResponse(null, null) : new IpLocationResponse(latitude, longitude);
    }

    private static decimal? Read(HttpRequest request, string header, int maxAbsolute) =>
        decimal.TryParse(request.Headers[header].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && Math.Abs(value) <= maxAbsolute
            ? Math.Round(value, 2, MidpointRounding.AwayFromZero)
            : null;
}
