using HockeyIndex.Api.Infrastructure.Text;
using HockeyIndex.Api.Integrations.Geocoding;

namespace HockeyIndex.Api.Integrations.Fakes;

/// <summary>
/// Deterministic geocoder over <see cref="Fixtures"/>. A query matches a fixture when every word of the query appears in its
/// normalized full address. A query containing <c>unavailable</c> fails as a provider fault. Call counts let tests assert
/// when the provider was (not) used.
/// </summary>
public sealed class FakeGeocoder : IGeocoder
{
    public const string ProviderFaultMarker = "unavailable";

    /// <summary>America/Toronto.</summary>
    public static readonly GeocodedAddress TorontoCarlton = new(
        "fake.mapbox.toronto-carlton",
        "50 Carlton Street, Toronto, Ontario M5B 1J2, Canada",
        "50 Carlton Street",
        "Toronto",
        "ON",
        "CA",
        43.66197,
        -79.38016);

    /// <summary>America/Toronto, about 87 m north of <see cref="TorontoCarlton"/>.</summary>
    public static readonly GeocodedAddress TorontoWood = new(
        "fake.mapbox.toronto-wood",
        "1 Wood Street, Toronto, Ontario M4Y 2P4, Canada",
        "1 Wood Street",
        "Toronto",
        "ON",
        "CA",
        43.66275,
        -79.38030);

    /// <summary>America/Los_Angeles.</summary>
    public static readonly GeocodedAddress SeattleFifthAvenue = new(
        "fake.mapbox.seattle-5th-ave",
        "10601 5th Avenue Northeast, Seattle, Washington 98125, United States",
        "10601 5th Avenue Northeast",
        "Seattle",
        "WA",
        "US",
        47.70574,
        -122.32266);

    /// <summary>America/Phoenix (no DST).</summary>
    public static readonly GeocodedAddress ScottsdaleBell = new(
        "fake.mapbox.scottsdale-bell",
        "9375 East Bell Road, Scottsdale, Arizona 85260, United States",
        "9375 East Bell Road",
        "Scottsdale",
        "AZ",
        "US",
        33.64039,
        -111.88357);

    public static readonly IReadOnlyList<GeocodedAddress> Fixtures = [TorontoCarlton, TorontoWood, SeattleFifthAvenue, ScottsdaleBell];

    private int suggestCalls;
    private int permanentCalls;

    public int SuggestCalls => Volatile.Read(ref suggestCalls);

    public int PermanentCalls => Volatile.Read(ref permanentCalls);

    public Task<GeocodeResult> SuggestAsync(string query, GeoCoordinate? proximity, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref suggestCalls);
        return Task.FromResult(Search(query, MapboxGeocoder.SuggestionLimit));
    }

    public Task<GeocodeResult> GeocodePermanentAsync(string fullAddress, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref permanentCalls);
        return Task.FromResult(Search(fullAddress, limit: 1));
    }

    private static GeocodeResult Search(string query, int limit)
    {
        var normalizedQuery = TextNormalizer.Normalize(query);
        if (normalizedQuery.Contains(ProviderFaultMarker, StringComparison.Ordinal))
        {
            return GeocodeResult.Failed(isProviderFault: true);
        }

        var words = normalizedQuery.Replace(",", " ", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return GeocodeResult.Found(Fixtures
            .Where(fixture => words.All(word => TextNormalizer.Normalize(fixture.FullAddress).Contains(word, StringComparison.Ordinal)))
            .Take(limit)
            .ToList());
    }
}

public static class FakeGeocoderSetup
{
    public static IServiceCollection AddFakeGeocoder(this IServiceCollection services) =>
        services.AddSingleton<IGeocoder, FakeGeocoder>();
}
