using NetTopologySuite.Geometries;

namespace HockeyIndex.Api.Domain;

public static class VenueProviders
{
    public const string Mapbox = "mapbox";
    public const string Geocodio = "geocodio";
    public const string Esri = "esri";
    public const string Osm = "osm";
    public const string Manual = "manual";
}

/// <summary>A facility (building). Events add an optional rink label (AC-VN-1).</summary>
public sealed class Venue
{
    public const int NameMaxLength = 120;
    public const int AddressLineMaxLength = 200;
    public const int CityMaxLength = 80;
    public const int TimeZoneMaxLength = 64;
    public const int ProviderPlaceIdMaxLength = 255;

    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string PublicId { get; init; }
    public required string Name { get; set; }
    public required string NameNormalized { get; set; }
    public required string AddressLine { get; set; }
    public required string City { get; set; }
    public required string Region { get; set; }
    public required string Country { get; set; }

    /// <summary>WGS 84 point (SRID 4326) stored as <c>geography(Point,4326)</c>; X is longitude, Y is latitude.</summary>
    public required Point Geo { get; set; }

    /// <summary>Canonical IANA (tzdb) zone id.</summary>
    public required string TimeZone { get; set; }

    public required string Provider { get; init; }
    public string? ProviderPlaceId { get; init; }
    public Guid? MergedIntoId { get; set; }
    public Guid? CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }
}
