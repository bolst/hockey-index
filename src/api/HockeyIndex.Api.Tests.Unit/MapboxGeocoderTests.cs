using HockeyIndex.Api.Integrations.Geocoding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class MapboxGeocoderTests : IDisposable
{
    private const string Token = "pk.test-token";
    private const string ForwardPath = "/search/geocode/v6/forward";
    private const string CarltonFeature = """
        {
          "type": "Feature",
          "properties": {
            "mapbox_id": "dXJuOm1ieGFkcjpjYXJsdG9u",
            "feature_type": "address",
            "name": "50 Carlton Street",
            "full_address": "50 Carlton Street, Toronto, Ontario M5B 1J2, Canada",
            "coordinates": { "longitude": -79.38016, "latitude": 43.66197 },
            "context": {
              "address": { "name": "50 Carlton Street" },
              "place": { "name": "Toronto" },
              "region": { "name": "Ontario", "region_code": "ON" },
              "country": { "name": "Canada", "country_code": "ca" }
            }
          }
        }
        """;

    private readonly WireMockServer server = WireMockServer.Start();
    private readonly HttpClient http = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Suggestions_request_temporary_us_ca_addresses_near_the_proximity_point()
    {
        Respond(200, $$"""{"type":"FeatureCollection","features":[{{CarltonFeature}}]}""");

        var result = await Geocoder().SuggestAsync("50 carlton", new GeoCoordinate(43.65, -79.38), Ct);

        Assert.True(result.Succeeded);
        var query = Assert.Single(server.LogEntries).RequestMessage!.Query!;
        Assert.Equal("50 carlton", query["q"].Single());
        Assert.Equal(["us", "ca"], query["country"]);
        Assert.Equal("address", query["types"].Single());
        Assert.Equal("5", query["limit"].Single());
        Assert.Equal("true", query["autocomplete"].Single());
        Assert.Equal("false", query["permanent"].Single());
        Assert.Equal(["-79.38", "43.65"], query["proximity"]);
        Assert.Equal(Token, query["access_token"].Single());
    }

    [Fact]
    public async Task Permanent_geocode_requests_one_permanent_non_autocomplete_result()
    {
        Respond(200, $$"""{"features":[{{CarltonFeature}}]}""");

        await Geocoder().GeocodePermanentAsync("50 Carlton Street, Toronto, Ontario M5B 1J2, Canada", Ct);

        var query = Assert.Single(server.LogEntries).RequestMessage!.Query!;
        Assert.Equal("50 Carlton Street, Toronto, Ontario M5B 1J2, Canada", string.Join(",", query["q"]));
        Assert.Equal("1", query["limit"].Single());
        Assert.Equal("false", query["autocomplete"].Single());
        Assert.Equal("true", query["permanent"].Single());
        Assert.False(query.ContainsKey("proximity"));
    }

    [Fact]
    public async Task Parses_address_fields_and_upper_cases_codes()
    {
        Respond(200, $$"""{"features":[{{CarltonFeature}}]}""");

        var result = await Geocoder().GeocodePermanentAsync("50 Carlton Street", Ct);

        Assert.Equal(
            new GeocodedAddress(
                "dXJuOm1ieGFkcjpjYXJsdG9u", "50 Carlton Street, Toronto, Ontario M5B 1J2, Canada", "50 Carlton Street", "Toronto", "ON", "CA",
                43.66197, -79.38016),
            Assert.Single(result.Addresses));
    }

    [Fact]
    public async Task Skips_features_missing_fields_a_venue_needs()
    {
        Respond(200, """
            {"features":[
              {"properties":{"mapbox_id":"no-region","full_address":"1 Main St","coordinates":{"longitude":-79,"latitude":43},
                "context":{"address":{"name":"1 Main St"},"place":{"name":"Toronto"},"country":{"country_code":"ca"}}}},
              {"properties":{"full_address":"2 Main St","coordinates":{"longitude":-79,"latitude":43}}},
              {}
            ]}
            """);

        var result = await Geocoder().SuggestAsync("main", proximity: null, Ct);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Addresses);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Provider_errors_fail_as_provider_faults(int status)
    {
        Respond(status, "{}");

        var result = await Geocoder().SuggestAsync("carlton", proximity: null, Ct);

        Assert.True(result is { Succeeded: false, IsProviderFault: true });
    }

    [Theory]
    [InlineData(400)]
    [InlineData(422)]
    public async Task Rejected_queries_fail_without_blaming_the_provider(int status)
    {
        Respond(status, """{"message":"bad query"}""");

        var result = await Geocoder().SuggestAsync("carlton", proximity: null, Ct);

        Assert.True(result is { Succeeded: false, IsProviderFault: false });
    }

    [Fact]
    public async Task Timeout_and_malformed_body_are_provider_faults()
    {
        server.Given(Request.Create().WithPath(ForwardPath).WithParam("q", "slow"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}").WithDelay(TimeSpan.FromSeconds(2)));
        server.Given(Request.Create().WithPath(ForwardPath).WithParam("q", "garbled"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("not json"));

        var geocoder = Geocoder(TimeSpan.FromMilliseconds(200));
        var slow = await geocoder.SuggestAsync("slow", proximity: null, Ct);
        var garbled = await geocoder.SuggestAsync("garbled", proximity: null, Ct);

        Assert.True(slow is { Succeeded: false, IsProviderFault: true });
        Assert.True(garbled is { Succeeded: false, IsProviderFault: true });
    }

    public void Dispose()
    {
        http.Dispose();
        server.Dispose();
    }

    private void Respond(int status, string body) =>
        server.Given(Request.Create().WithPath(ForwardPath).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

    private MapboxGeocoder Geocoder(TimeSpan? timeout = null) =>
        new(
            http,
            Options.Create(new MapboxOptions { AccessToken = Token, BaseUrl = new Uri(server.Url!), Timeout = timeout ?? TimeSpan.FromSeconds(5) }),
            NullLogger<MapboxGeocoder>.Instance);
}
