using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Venues;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using HockeyIndex.Api.Infrastructure.Text;
using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Integrations.Geocoding;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class VenueTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private const double MetersPerDegreeLatitude = 111_195;
    private static readonly GeocodedAddress Carlton = FakeGeocoder.TorontoCarlton;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unauthenticated_requests_are_rejected_with_401()
    {
        using var client = Factory.CreateHostClient();

        using var autocomplete = await AutocompleteAsync(client, "carlton");
        using var create = await CreateAsync(client, "Mattamy Athletic Centre", Carlton);

        Assert.Equal(HttpStatusCode.Unauthorized, autocomplete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    [Fact]
    public async Task Requests_without_the_csrf_header_are_rejected_with_403()
    {
        using var client = Factory.CreateHostClient();
        await client.SignInWithPhoneAsync(Phone(1));
        client.DefaultRequestHeaders.Remove(RequestedWithGuard.HeaderName);

        using var autocomplete = await AutocompleteAsync(client, "carlton");
        using var create = await CreateAsync(client, "Mattamy Athletic Centre", Carlton);

        Assert.Equal(HttpStatusCode.Forbidden, autocomplete.StatusCode);
        Assert.Equal("csrf_rejected", await ProblemCodeAsync(autocomplete));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("csrf_rejected", await ProblemCodeAsync(create));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("   b   ")]
    public async Task Autocomplete_rejects_queries_shorter_than_two_characters(string query)
    {
        using var client = await SignedInClientAsync();

        using var response = await AutocompleteAsync(client, query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_query", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Autocomplete_lists_database_venues_by_similarity_before_provider_suggestions()
    {
        await SeedVenueAsync("Scotiabank Pond North Rink", 43.80, -79.50);
        var exact = await SeedVenueAsync("Scotiabank Pond", 43.79, -79.49);
        await SeedVenueAsync("Ford Performance Centre", 43.62, -79.52);
        using var client = await SignedInClientAsync();

        using var response = await AutocompleteAsync(client, "scotiabank pond");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var venues = body.GetProperty("venues").EnumerateArray().ToList();
        Assert.Equal(["Scotiabank Pond", "Scotiabank Pond North Rink"], venues.Select(venue => venue.GetProperty("name").GetString()));
        Assert.Equal(exact.PublicId, venues[0].GetProperty("publicId").GetString());
        Assert.Equal(SuggestionsStatus.Ok, body.GetProperty("suggestionsStatus").GetString());
        Assert.Equal(1, Geocoder().SuggestCalls);
    }

    [Fact]
    public async Task Autocomplete_breaks_similarity_ties_by_distance_from_the_search_point()
    {
        var far = await SeedVenueAsync("Canlan Ice Sports", 47.70, -122.32);
        var near = await SeedVenueAsync("Canlan Ice Sports", 43.66, -79.38);
        using var client = await SignedInClientAsync();

        using var response = await AutocompleteAsync(client, "canlan ice sports", latitude: 43.67, longitude: -79.39);

        var venues = (await ReadJsonAsync(response)).GetProperty("venues").EnumerateArray().ToList();
        Assert.Equal([near.PublicId, far.PublicId], venues.Select(venue => venue.GetProperty("publicId").GetString()));
        Assert.InRange(venues[0].GetProperty("distanceMeters").GetDouble(), 1_000, 2_000);
    }

    [Fact]
    public async Task Autocomplete_skips_the_provider_when_the_database_returns_five_venues()
    {
        for (var i = 0; i < AutocompleteVenues.Limit + 1; i++)
        {
            await SeedVenueAsync("Canlan Ice Sports", 43.6 + (i * 0.01), -79.4);
        }

        using var client = await SignedInClientAsync();

        using var response = await AutocompleteAsync(client, "canlan ice sports");

        var body = await ReadJsonAsync(response);
        Assert.Equal(AutocompleteVenues.Limit, body.GetProperty("venues").GetArrayLength());
        Assert.Equal(0, body.GetProperty("suggestions").GetArrayLength());
        Assert.Equal(SuggestionsStatus.NotNeeded, body.GetProperty("suggestionsStatus").GetString());
        Assert.Equal(0, Geocoder().SuggestCalls);
        Assert.Equal(0, await ProviderCallsAsync(ProviderNames.MapboxTemp));
    }

    [Fact]
    public async Task Autocomplete_returns_provider_suggestions_without_storing_them()
    {
        using var client = await SignedInClientAsync();

        using var response = await AutocompleteAsync(client, "50 Carlton Street Toronto");

        var body = await ReadJsonAsync(response);
        Assert.Equal(0, body.GetProperty("venues").GetArrayLength());
        Assert.Equal(SuggestionsStatus.Ok, body.GetProperty("suggestionsStatus").GetString());
        var suggestion = Assert.Single(body.GetProperty("suggestions").EnumerateArray());
        Assert.Equal(Carlton.ProviderPlaceId, suggestion.GetProperty("providerPlaceId").GetString());
        Assert.Equal(Carlton.FullAddress, suggestion.GetProperty("label").GetString());
        Assert.Equal("ON", suggestion.GetProperty("region").GetString());
        Assert.Equal(Carlton.Latitude, suggestion.GetProperty("latitude").GetDouble());
        Assert.Equal(1, await ProviderCallsAsync(ProviderNames.MapboxTemp));
        await using var db = Postgis.CreateDbContext();
        Assert.Equal(0, await db.Venues.CountAsync(Ct));
    }

    [Fact]
    public async Task Autocomplete_with_the_breaker_open_returns_database_venues_only()
    {
        var venue = await SeedVenueAsync("Scotiabank Pond", 43.79, -79.49);
        await OpenMapboxBreakerAsync(Factory);
        using var client = await SignedInClientAsync();

        using var response = await AutocompleteAsync(client, "scotiabank pond");

        var body = await ReadJsonAsync(response);
        Assert.Equal(venue.PublicId, Assert.Single(body.GetProperty("venues").EnumerateArray()).GetProperty("publicId").GetString());
        Assert.Equal(SuggestionsStatus.Unavailable, body.GetProperty("suggestionsStatus").GetString());
        Assert.Equal(0, Geocoder().SuggestCalls);
        Assert.Equal(0, await ProviderCallsAsync(ProviderNames.MapboxTemp));
    }

    [Fact]
    public async Task Provider_fault_opens_the_breaker_so_later_lookups_stay_database_only()
    {
        using var client = await SignedInClientAsync();

        using var faulted = await AutocompleteAsync(client, $"{FakeGeocoder.ProviderFaultMarker} street");
        using var later = await AutocompleteAsync(client, "carlton street");

        Assert.Equal(SuggestionsStatus.Unavailable, (await ReadJsonAsync(faulted)).GetProperty("suggestionsStatus").GetString());
        Assert.Equal(SuggestionsStatus.Unavailable, (await ReadJsonAsync(later)).GetProperty("suggestionsStatus").GetString());
        Assert.Equal(1, Geocoder().SuggestCalls);
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<BreakerService>().IsOpenAsync(BreakerNames.Mapbox, Ct));
    }

    [Fact]
    public async Task Autocomplete_reports_limit_reached_when_the_per_host_lookup_budget_is_spent()
    {
        var factory = CreateFactory(new Dictionary<string, string?> { ["Providers:Caps:mapbox_temp:PerHost"] = "1" });
        using var client = factory.CreateHostClient();
        await client.SignInWithPhoneAsync(Phone(1));

        using var first = await AutocompleteAsync(client, "carlton street");
        using var second = await AutocompleteAsync(client, "wood street");

        Assert.Equal(SuggestionsStatus.Ok, (await ReadJsonAsync(first)).GetProperty("suggestionsStatus").GetString());
        Assert.Equal(SuggestionsStatus.LimitReached, (await ReadJsonAsync(second)).GetProperty("suggestionsStatus").GetString());
        Assert.Equal(1, Geocoder(factory).SuggestCalls);
    }

    [Theory]
    [MemberData(nameof(TimeZoneFixtures))]
    public async Task Create_geocodes_once_and_stores_the_venue_with_its_time_zone(string fixtureId, string expectedTimeZone)
    {
        var fixture = FakeGeocoder.Fixtures.Single(candidate => candidate.ProviderPlaceId == fixtureId);
        using var client = Factory.CreateHostClient();
        var hostId = await client.SignInWithPhoneAsync(Phone(1));

        using var response = await CreateAsync(client, "  Home  Rink ", fixture);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var publicId = body.GetProperty("publicId").GetString()!;
        Assert.Equal($"/v1/venues/{publicId}", response.Headers.Location?.OriginalString);
        Assert.Equal(expectedTimeZone, body.GetProperty("timeZone").GetString());
        Assert.Equal(1, Geocoder().PermanentCalls);
        Assert.Equal(1, await ProviderCallsAsync(ProviderNames.MapboxPerm));

        await using var db = Postgis.CreateDbContext();
        var venue = await db.Venues.SingleAsync(Ct);
        Assert.Equal(publicId, venue.PublicId);
        Assert.Equal("Home  Rink", venue.Name);
        Assert.Equal("home rink", venue.NameNormalized);
        Assert.Equal(expectedTimeZone, venue.TimeZone);
        Assert.Equal(fixture.Latitude, venue.Geo.Y, 6);
        Assert.Equal(fixture.Longitude, venue.Geo.X, 6);
        Assert.Equal(4326, venue.Geo.SRID);
        Assert.Equal(fixture.AddressLine, venue.AddressLine);
        Assert.Equal(fixture.City, venue.City);
        Assert.Equal(fixture.Region, venue.Region);
        Assert.Equal(fixture.Country, venue.Country);
        Assert.Equal(VenueProviders.Mapbox, venue.Provider);
        Assert.Equal(fixture.ProviderPlaceId, venue.ProviderPlaceId);
        Assert.Equal(hostId, venue.CreatedBy);
    }

    public static TheoryData<string, string> TimeZoneFixtures => new()
    {
        { FakeGeocoder.TorontoCarlton.ProviderPlaceId, "America/Toronto" },
        { FakeGeocoder.SeattleFifthAvenue.ProviderPlaceId, "America/Los_Angeles" },
        { FakeGeocoder.ScottsdaleBell.ProviderPlaceId, "America/Phoenix" },
    };

    [Fact]
    public async Task Create_reuses_the_venue_with_the_same_provider_place_id_without_a_paid_call()
    {
        var existing = await SeedVenueAsync("Mattamy Athletic Centre", Carlton.Latitude, Carlton.Longitude, Carlton.ProviderPlaceId);
        using var client = await SignedInClientAsync();

        using var response = await CreateAsync(client, "Totally Different Name", Carlton);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(existing.PublicId, (await ReadJsonAsync(response)).GetProperty("publicId").GetString());
        Assert.Equal(0, Geocoder().PermanentCalls);
        Assert.Equal(0, await ProviderCallsAsync(ProviderNames.MapboxPerm));
        await using var db = Postgis.CreateDbContext();
        Assert.Equal(1, await db.Venues.CountAsync(Ct));
    }

    [Fact]
    public async Task Create_reuse_follows_a_merged_venue_to_its_target()
    {
        var target = await SeedVenueAsync("Mattamy Athletic Centre", Carlton.Latitude, Carlton.Longitude);
        await SeedVenueAsync("Maple Leaf Gardens", Carlton.Latitude, Carlton.Longitude, Carlton.ProviderPlaceId, mergedIntoId: target.Id);
        using var client = await SignedInClientAsync();

        using var response = await CreateAsync(client, "Maple Leaf Gardens", Carlton);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(target.PublicId, (await ReadJsonAsync(response)).GetProperty("publicId").GetString());
    }

    [Fact]
    public async Task Create_near_a_similarly_named_venue_returns_candidates_until_confirmed()
    {
        var existing = await SeedVenueAsync("Mattamy Athletic Centre", NorthOf(Carlton, 120), Carlton.Longitude);
        using var client = await SignedInClientAsync();

        using var conflict = await CreateAsync(client, "Mattamy Athletic Center", Carlton);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await ReadJsonAsync(conflict);
        Assert.Equal("venue_candidates", problem.GetProperty("code").GetString());
        var candidate = Assert.Single(problem.GetProperty("candidates").EnumerateArray());
        Assert.Equal(existing.PublicId, candidate.GetProperty("publicId").GetString());
        Assert.InRange(candidate.GetProperty("distanceMeters").GetDouble(), 110, 130);
        Assert.Equal(0, Geocoder().PermanentCalls);

        using var confirmed = await CreateAsync(client, "Mattamy Athletic Center", Carlton, confirmNew: true);

        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        await using var db = Postgis.CreateDbContext();
        Assert.Equal(2, await db.Venues.CountAsync(Ct));
    }

    [Fact]
    public async Task Create_200_metres_from_a_similarly_named_venue_creates_a_new_venue()
    {
        await SeedVenueAsync("Mattamy Athletic Centre", NorthOf(Carlton, 200), Carlton.Longitude);
        using var client = await SignedInClientAsync();

        using var response = await CreateAsync(client, "Mattamy Athletic Centre", Carlton);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_near_a_differently_named_venue_creates_a_new_venue()
    {
        await SeedVenueAsync("Ryerson Recreation Hall", NorthOf(Carlton, 100), Carlton.Longitude);
        using var client = await SignedInClientAsync();

        using var response = await CreateAsync(client, "Mattamy Athletic Centre", Carlton);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_a_permanent_result_more_than_50_metres_from_the_selection()
    {
        using var client = await SignedInClientAsync();
        var moved = Carlton with { ProviderPlaceId = "suggestion.moved", Latitude = NorthOf(Carlton, 60) };

        using var response = await CreateAsync(client, "Mattamy Athletic Centre", moved);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("location_mismatch", await ProblemCodeAsync(response));
        await using var db = Postgis.CreateDbContext();
        Assert.Equal(0, await db.Venues.CountAsync(Ct));
    }

    [Fact]
    public async Task Create_accepts_a_permanent_result_within_50_metres_and_stores_the_permanent_id()
    {
        using var client = await SignedInClientAsync();
        var suggestion = Carlton with { ProviderPlaceId = "suggestion.temporary-id", Latitude = NorthOf(Carlton, 40) };

        using var response = await CreateAsync(client, "Mattamy Athletic Centre", suggestion);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = Postgis.CreateDbContext();
        var venue = await db.Venues.SingleAsync(Ct);
        Assert.Equal(Carlton.ProviderPlaceId, venue.ProviderPlaceId);
        Assert.Equal(Carlton.Latitude, venue.Geo.Y, 6);
    }

    [Fact]
    public async Task Create_rejects_an_address_the_provider_cannot_confirm()
    {
        using var client = await SignedInClientAsync();
        var unknown = Carlton with { ProviderPlaceId = "suggestion.unknown", FullAddress = "1 Nowhere Lane, Atlantis" };

        using var response = await CreateAsync(client, "Mattamy Athletic Centre", unknown);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("address_not_found", await ProblemCodeAsync(response));
    }

    [Theory]
    [InlineData("", "id", "addr", 43.0, -79.0)]
    [InlineData("Rink", "", "addr", 43.0, -79.0)]
    [InlineData("Rink", "id", "", 43.0, -79.0)]
    [InlineData("Rink", "id", "addr", 91.0, -79.0)]
    [InlineData("Rink", "id", "addr", 43.0, 181.0)]
    public async Task Create_rejects_invalid_input(string name, string providerPlaceId, string address, double latitude, double longitude)
    {
        using var client = await SignedInClientAsync();

        using var response = await client.PostAsJsonAsync(
            "/v1/venues", new { name, providerPlaceId, address, latitude, longitude }, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("invalid_venue", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Create_with_the_breaker_open_returns_503_without_a_paid_call()
    {
        await OpenMapboxBreakerAsync(Factory);
        using var client = await SignedInClientAsync();

        using var response = await CreateAsync(client, "Mattamy Athletic Centre", Carlton);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("venue_provider_unavailable", await ProblemCodeAsync(response));
        Assert.Equal(0, Geocoder().PermanentCalls);
        Assert.Equal(0, await ProviderCallsAsync(ProviderNames.MapboxPerm));
    }

    [Fact]
    public async Task Create_provider_fault_opens_the_breaker_and_returns_503()
    {
        using var client = await SignedInClientAsync();
        var faulty = Carlton with { ProviderPlaceId = "suggestion.faulty", FullAddress = $"{FakeGeocoder.ProviderFaultMarker} street" };

        using var response = await CreateAsync(client, "Mattamy Athletic Centre", faulty);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<BreakerService>().IsOpenAsync(BreakerNames.Mapbox, Ct));
    }

    [Fact]
    public async Task Create_refuses_with_429_when_the_per_host_venue_budget_is_spent()
    {
        var factory = CreateFactory(new Dictionary<string, string?> { ["Providers:Caps:mapbox_perm:PerHost"] = "1" });
        using var client = factory.CreateHostClient();
        await client.SignInWithPhoneAsync(Phone(1));

        using var first = await CreateAsync(client, "Mattamy Athletic Centre", Carlton);
        using var second = await CreateAsync(client, "Bell Rink", FakeGeocoder.ScottsdaleBell);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("venue_limit_reached", await ProblemCodeAsync(second));
        Assert.Equal(1, Geocoder(factory).PermanentCalls);
    }

    [Fact]
    public async Task Create_refuses_with_429_when_the_global_venue_budget_is_spent()
    {
        var factory = CreateFactory(new Dictionary<string, string?> { ["Providers:Caps:mapbox_perm:Global"] = "1" });
        using var firstHost = factory.CreateHostClient();
        await firstHost.SignInWithPhoneAsync(Phone(1));
        using var secondHost = factory.CreateHostClient("203.0.113.11");
        await secondHost.SignInWithPhoneAsync(Phone(2));

        using var first = await CreateAsync(firstHost, "Mattamy Athletic Centre", Carlton);
        using var second = await CreateAsync(secondHost, "Bell Rink", FakeGeocoder.ScottsdaleBell);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(1, Geocoder(factory).PermanentCalls);
    }

    [Fact]
    public async Task Concurrent_creates_of_the_same_place_insert_one_venue()
    {
        using var firstHost = Factory.CreateHostClient();
        await firstHost.SignInWithPhoneAsync(Phone(1));
        using var secondHost = Factory.CreateHostClient("203.0.113.11");
        await secondHost.SignInWithPhoneAsync(Phone(2));

        var responses = await Task.WhenAll(
            CreateAsync(firstHost, "Mattamy Athletic Centre", Carlton),
            CreateAsync(secondHost, "Mattamy Athletic Centre", Carlton));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Created],
            responses.Select(response => response.StatusCode).Order());
        var publicIds = await Task.WhenAll(responses.Select(async response => (await ReadJsonAsync(response)).GetProperty("publicId").GetString()));
        Assert.Single(publicIds.Distinct());
        await using var db = Postgis.CreateDbContext();
        Assert.Equal(1, await db.Venues.CountAsync(Ct));
        DisposeAll(responses);
    }

    [Fact]
    public async Task Concurrent_creates_of_nearby_similarly_named_venues_insert_one_venue()
    {
        using var firstHost = Factory.CreateHostClient();
        await firstHost.SignInWithPhoneAsync(Phone(1));
        using var secondHost = Factory.CreateHostClient("203.0.113.11");
        await secondHost.SignInWithPhoneAsync(Phone(2));

        var responses = await Task.WhenAll(
            CreateAsync(firstHost, "Mattamy Athletic Centre", Carlton),
            CreateAsync(secondHost, "Mattamy Athletic Center", FakeGeocoder.TorontoWood));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(response => response.StatusCode).Order());
        await using var db = Postgis.CreateDbContext();
        Assert.Equal(1, await db.Venues.CountAsync(Ct));
        DisposeAll(responses);
    }

    [Fact]
    public async Task Get_returns_a_venue_by_public_id_and_404_otherwise()
    {
        var venue = await SeedVenueAsync("Mattamy Athletic Centre", Carlton.Latitude, Carlton.Longitude);
        using var client = await SignedInClientAsync();

        using var found = await client.GetAsync(new Uri($"/v1/venues/{venue.PublicId}", UriKind.Relative), Ct);
        using var missing = await client.GetAsync(new Uri($"/v1/venues/{PublicId.New()}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var body = await ReadJsonAsync(found);
        Assert.Equal("Mattamy Athletic Centre", body.GetProperty("name").GetString());
        Assert.Equal("America/Toronto", body.GetProperty("timeZone").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("distanceMeters").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("venue_not_found", await ProblemCodeAsync(missing));
    }

    private static double NorthOf(GeocodedAddress origin, double meters) => origin.Latitude + (meters / MetersPerDegreeLatitude);

    private static void DisposeAll(IEnumerable<HttpResponseMessage> responses)
    {
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private static Task<HttpResponseMessage> AutocompleteAsync(HttpClient client, string query, double? latitude = null, double? longitude = null)
    {
        var url = $"/v1/venues/autocomplete?q={Uri.EscapeDataString(query)}";
        if (latitude is not null && longitude is not null)
        {
            url += FormattableString.Invariant($"&lat={latitude}&lng={longitude}");
        }

        return client.GetAsync(new Uri(url, UriKind.Relative), Ct);
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string name, GeocodedAddress selected, bool confirmNew = false) =>
        client.PostAsJsonAsync(
            "/v1/venues",
            new CreateVenueRequest(name, selected.ProviderPlaceId, selected.FullAddress, selected.Latitude, selected.Longitude, confirmNew),
            Ct);

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = Factory.CreateHostClient();
        await client.SignInWithPhoneAsync(Phone(1));
        return client;
    }

    private FakeGeocoder Geocoder(ApiFactory? factory = null) =>
        (FakeGeocoder)(factory ?? Factory).Services.GetRequiredService<IGeocoder>();

    private async Task<Venue> SeedVenueAsync(
        string name, double latitude, double longitude, string? providerPlaceId = null, Guid? mergedIntoId = null)
    {
        var venue = new Venue
        {
            Id = Guid.CreateVersion7(),
            PublicId = PublicId.New(),
            Name = name,
            NameNormalized = TextNormalizer.Normalize(name),
            AddressLine = "1 Test Street",
            City = "Toronto",
            Region = "ON",
            Country = "CA",
            Geo = VenueGeometry.Point(latitude, longitude),
            TimeZone = "America/Toronto",
            Provider = VenueProviders.Mapbox,
            ProviderPlaceId = providerPlaceId,
            MergedIntoId = mergedIntoId,
            CreatedAt = Factory.Clock.GetCurrentInstant().ToDateTimeOffset(),
            UpdatedAt = Factory.Clock.GetCurrentInstant().ToDateTimeOffset(),
        };
        await using var db = Postgis.CreateDbContext();
        db.Venues.Add(venue);
        await db.SaveChangesAsync(Ct);
        return venue;
    }

    private async Task<int> ProviderCallsAsync(string provider)
    {
        await using var db = Postgis.CreateDbContext();
        return await db.ProviderUsage
            .Where(usage => usage.Provider == provider && usage.HostId == ProviderUsage.GlobalHostId)
            .SumAsync(usage => usage.Calls, Ct);
    }

    private static async Task OpenMapboxBreakerAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BreakerService>().OpenAsync(
            BreakerNames.Mapbox,
            factory.Clock.GetCurrentInstant().ToDateTimeOffset().AddHours(1),
            "test",
            BreakerOpener.Admin,
            Ct);
    }
}
