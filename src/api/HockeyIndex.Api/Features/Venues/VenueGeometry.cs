using NetTopologySuite.Geometries;

namespace HockeyIndex.Api.Features.Venues;

public static class VenueGeometry
{
    public const int Wgs84Srid = 4326;
    private const double EarthRadiusMeters = 6_371_008.8;

    public static bool IsValid(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    public static Point Point(double latitude, double longitude) => new(longitude, latitude) { SRID = Wgs84Srid };

    /// <summary>Great-circle (haversine) distance; within a few metres of PostGIS geography at venue scales.</summary>
    public static double DistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        var dLat = DegreesToRadians(latitude2 - latitude1);
        var dLng = DegreesToRadians(longitude2 - longitude1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
            + (Math.Cos(DegreesToRadians(latitude1)) * Math.Cos(DegreesToRadians(latitude2)) * Math.Pow(Math.Sin(dLng / 2), 2));
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
