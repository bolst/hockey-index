using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Venues;
using HockeyIndex.Api.Infrastructure.Time;
using HockeyIndex.Api.Integrations.Fakes;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class VenueSupportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(43.66197, -79.38016, "America/Toronto")]
    [InlineData(45.50, -73.57, "America/Toronto")]
    [InlineData(50.45, -104.61, "America/Regina")]
    [InlineData(47.70574, -122.32266, "America/Los_Angeles")]
    [InlineData(33.64039, -111.88357, "America/Phoenix")]
    [InlineData(41.88, -87.63, "America/Chicago")]
    [InlineData(61.22, -149.90, "America/Anchorage")]
    public void Time_zone_lookup_returns_the_iana_zone_for_coordinates(double latitude, double longitude, string expected) =>
        Assert.Equal(expected, TimeZoneLookup.Find(latitude, longitude));

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    [InlineData(double.NaN, 0)]
    public void Time_zone_lookup_rejects_invalid_coordinates(double latitude, double longitude) =>
        Assert.Null(TimeZoneLookup.Find(latitude, longitude));

    [Theory]
    [InlineData("America/Montreal", "America/Toronto")]
    [InlineData("US/Pacific", "America/Los_Angeles")]
    [InlineData("America/Toronto", "America/Toronto")]
    [InlineData("Not/AZone", null)]
    [InlineData(null, null)]
    public void Time_zone_ids_are_canonicalized_against_tzdb(string? zoneId, string? expected) =>
        Assert.Equal(expected, TimeZoneLookup.Canonicalize(zoneId));

    [Fact]
    public void Public_ids_are_ten_rfc4648_base32_characters_matching_the_pages_function_route()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => PublicId.New()).ToList();

        Assert.All(ids, id => Assert.Matches("^[a-z2-7]{10}$", id));
        Assert.All(ids, id => Assert.True(PublicId.IsValid(id)));
        Assert.False(PublicId.IsValid("abcdefghi1"));
        Assert.False(PublicId.IsValid("ABCDEFGHIJ"));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Haversine_distance_matches_known_offsets()
    {
        var carlton = FakeGeocoder.TorontoCarlton;
        var wood = FakeGeocoder.TorontoWood;

        Assert.InRange(VenueGeometry.DistanceMeters(carlton.Latitude, carlton.Longitude, wood.Latitude, wood.Longitude), 85, 90);
        Assert.Equal(0, VenueGeometry.DistanceMeters(43, -79, 43, -79));
    }

    [Fact]
    public void Venue_points_use_wgs84_with_longitude_as_x()
    {
        var point = VenueGeometry.Point(43.5, -79.25);

        Assert.Equal((-79.25, 43.5, 4326), (point.X, point.Y, point.SRID));
    }

    [Fact]
    public void Fake_fixtures_cover_a_canadian_and_us_time_zones()
    {
        Assert.Equal(
            ["America/Toronto", "America/Toronto", "America/Los_Angeles", "America/Phoenix"],
            FakeGeocoder.Fixtures.Select(fixture => TimeZoneLookup.Find(fixture.Latitude, fixture.Longitude)));
    }

    [Fact]
    public async Task Fake_geocoder_matches_every_query_word_and_counts_calls()
    {
        var geocoder = new FakeGeocoder();

        var toronto = await geocoder.SuggestAsync("Toronto, Ontario", proximity: null, Ct);
        var scottsdale = await geocoder.SuggestAsync("bell road scottsdale", proximity: null, Ct);
        var none = await geocoder.SuggestAsync("bell road toronto", proximity: null, Ct);
        var permanent = await geocoder.GeocodePermanentAsync(FakeGeocoder.SeattleFifthAvenue.FullAddress, Ct);

        Assert.Equal([FakeGeocoder.TorontoCarlton, FakeGeocoder.TorontoWood], toronto.Addresses);
        Assert.Equal([FakeGeocoder.ScottsdaleBell], scottsdale.Addresses);
        Assert.True(none.Succeeded);
        Assert.Empty(none.Addresses);
        Assert.Equal([FakeGeocoder.SeattleFifthAvenue], permanent.Addresses);
        Assert.Equal((3, 1), (geocoder.SuggestCalls, geocoder.PermanentCalls));
    }

    [Fact]
    public async Task Fake_geocoder_fails_as_a_provider_fault_on_the_marker()
    {
        var result = await new FakeGeocoder().SuggestAsync($"{FakeGeocoder.ProviderFaultMarker} rink", proximity: null, Ct);

        Assert.True(result is { Succeeded: false, IsProviderFault: true });
    }
}
