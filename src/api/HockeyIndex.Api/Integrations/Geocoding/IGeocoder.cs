namespace HockeyIndex.Api.Integrations.Geocoding;

public sealed record GeoCoordinate(double Latitude, double Longitude);

/// <param name="ProviderPlaceId">The provider's stable id (Mapbox <c>mapbox_id</c>).</param>
/// <param name="FullAddress">One-line display address; also the query for the permanent lookup.</param>
/// <param name="Country">ISO 3166-1 alpha-2, upper case.</param>
/// <param name="Region">Two-letter state or province code, upper case.</param>
public sealed record GeocodedAddress(
    string ProviderPlaceId,
    string FullAddress,
    string AddressLine,
    string City,
    string Region,
    string Country,
    double Latitude,
    double Longitude);

/// <param name="IsProviderFault">True when the failure points at the provider (5xx, timeout, auth, quota) and should open the breaker.</param>
public sealed record GeocodeResult(bool Succeeded, IReadOnlyList<GeocodedAddress> Addresses, bool IsProviderFault = false)
{
    public static GeocodeResult Found(IReadOnlyList<GeocodedAddress> addresses) => new(true, addresses);

    public static GeocodeResult Failed(bool isProviderFault) => new(false, [], isProviderFault);
}

/// <summary>
/// US/CA address geocoding. Implementations MUST NOT throw for provider errors; they return <see cref="GeocodeResult.Failed"/>.
/// </summary>
public interface IGeocoder
{
    /// <summary>Type-ahead suggestions under temporary-use terms: display only, MUST NOT be stored.</summary>
    Task<GeocodeResult> SuggestAsync(string query, GeoCoordinate? proximity, CancellationToken cancellationToken);

    /// <summary>One permanent-use lookup for the address a host selected; the result may be stored.</summary>
    Task<GeocodeResult> GeocodePermanentAsync(string fullAddress, CancellationToken cancellationToken);
}
