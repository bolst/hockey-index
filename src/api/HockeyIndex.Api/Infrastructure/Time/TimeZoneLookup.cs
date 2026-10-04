using NodaTime.TimeZones;
using GeoTimeZoneLookup = GeoTimeZone.TimeZoneLookup;

namespace HockeyIndex.Api.Infrastructure.Time;

/// <summary>Offline coordinate-to-IANA-zone lookup (GeoTimeZone), validated and canonicalized against NodaTime's tzdb.</summary>
public static class TimeZoneLookup
{
    /// <returns>The canonical tzdb id, or <c>null</c> when the coordinates are invalid or the zone is unknown to NodaTime.</returns>
    public static string? Find(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return null;
        }

        return Canonicalize(GeoTimeZoneLookup.GetTimeZone(latitude, longitude).Result);
    }

    /// <summary>Maps tzdb links (e.g. <c>America/Montreal</c>) to their canonical zone; unknown ids return <c>null</c>.</summary>
    public static string? Canonicalize(string? zoneId) =>
        zoneId is not null && TzdbDateTimeZoneSource.Default.CanonicalIdMap.TryGetValue(zoneId, out var canonical) ? canonical : null;
}
