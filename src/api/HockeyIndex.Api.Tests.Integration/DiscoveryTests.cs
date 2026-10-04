using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Discovery;
using HockeyIndex.Api.Features.Venues;
using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Text;
using HockeyIndex.Api.Infrastructure.Time;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using NpgsqlTypes;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.EventTestSupport;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class DiscoveryTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private const string WindowStart = "2026-10-03T12:00:00Z";
    private static readonly VenueTimeConverter Times = new(DateTimeZoneProviders.Tzdb);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset Now(ApiFactory factory) => factory.Clock.GetCurrentInstant().ToDateTimeOffset();

    private static Uri Search(string filters = "", int radius = 25, int days = 14, string from = WindowStart) =>
        new($"/v1/search?lat=43.66&lng=-79.38&r={radius}&from={from}&days={days}{filters}", UriKind.Relative);

    [Fact]
    public async Task Search_returns_events_within_the_radius_with_distance()
    {
        var (hostId, near) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var fourteenMiles = await SeedVenueAtAsync(db, 43.86, -79.38);
        var sixtyMiles = await SeedVenueAtAsync(db, 44.5, -79.38);
        var a = await SeedEventAsync(db, hostId, near, Local(10, 5, 19));
        var b = await SeedEventAsync(db, hostId, fourteenMiles, Local(10, 6, 19));
        await SeedEventAsync(db, hostId, sixtyMiles, Local(10, 7, 19));

        Assert.Equal([a.PublicId], await SearchIdsAsync(Search(radius: 10)));
        Assert.Equal([a.PublicId, b.PublicId], await SearchIdsAsync(Search(radius: 25)));

        var events = (await SearchAsync(Search(radius: 25))).GetProperty("events");
        Assert.Equal(0.0, events[0].GetProperty("distanceMiles").GetDouble());
        Assert.Equal(13.8, events[1].GetProperty("distanceMiles").GetDouble(), 0.2);
    }

    [Fact]
    public async Task Search_matches_date_overlap_and_orders_by_effective_start()
    {
        var (hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var oct5 = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19));
        var oct4 = await SeedEventAsync(db, hostId, venue, Local(10, 4, 19));
        var league = await SeedEventAsync(
            db, hostId, venue, new DateTime(2026, 9, 1, 0, 0, 0), new DateTime(2026, 12, 2, 0, 0, 0), type: EventType.League);
        var oct20 = await SeedEventAsync(db, hostId, venue, Local(10, 20, 19));
        var nextSpring = await SeedEventAsync(db, hostId, venue, new DateTime(2027, 4, 1, 19, 0, 0));

        Assert.Equal([league.PublicId, oct4.PublicId, oct5.PublicId], await SearchIdsAsync(Search(days: 14)));
        Assert.Equal([league.PublicId, oct4.PublicId, oct5.PublicId, oct20.PublicId], await SearchIdsAsync(Search(days: 30)));
        Assert.Equal([league.PublicId, oct20.PublicId], await SearchIdsAsync(Search(days: 7, from: "2026-10-15T00:00:00Z")));
        Assert.Equal([oct4.PublicId], await SearchIdsAsync(Search("&type=scrimmage", days: 1, from: "2026-10-04T12:00:00Z")));
        Assert.DoesNotContain(nextSpring.PublicId, await SearchIdsAsync(Search(days: 30)));

        var leagueResult = (await SearchAsync(Search(days: 14))).GetProperty("events")[0];
        Assert.Equal("league", leagueResult.GetProperty("type").GetString());
        Assert.Equal("2026-09-01", leagueResult.GetProperty("startDate").GetString());
        Assert.Equal("2026-12-01", leagueResult.GetProperty("endDate").GetString());
    }

    [Fact]
    public async Task Search_matches_skill_ranges_by_overlap()
    {
        var (hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var evt = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), skill: (3, 5));

        Assert.Equal([evt.PublicId], await SearchIdsAsync(Search("&smin=4&smax=4")));
        Assert.Equal([evt.PublicId], await SearchIdsAsync(Search("&smin=5&smax=7")));
        Assert.Equal([evt.PublicId], await SearchIdsAsync(Search("&smin=0&smax=3")));
        Assert.Empty(await SearchIdsAsync(Search("&smin=6&smax=7")));
        Assert.Empty(await SearchIdsAsync(Search("&smin=0&smax=2")));

        var skill = (await SearchAsync(Search())).GetProperty("events")[0].GetProperty("skill");
        Assert.Equal(3, skill.GetProperty("min").GetInt32());
        Assert.Equal(5, skill.GetProperty("max").GetInt32());
    }

    [Fact]
    public async Task Search_filters_by_type()
    {
        var (hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var scrimmage = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19));
        var tournament = await SeedEventAsync(
            db, hostId, venue, new DateTime(2026, 10, 10, 0, 0, 0), new DateTime(2026, 10, 12, 0, 0, 0), type: EventType.Tournament);

        Assert.Equal([scrimmage.PublicId], await SearchIdsAsync(Search("&type=scrimmage")));
        Assert.Equal([tournament.PublicId], await SearchIdsAsync(Search("&type=tournament")));
        Assert.Empty(await SearchIdsAsync(Search("&type=league")));
        Assert.Equal([scrimmage.PublicId, tournament.PublicId], await SearchIdsAsync(Search()));
    }

    [Fact]
    public async Task Search_filters_by_fee_bucket_and_excludes_unknown_fees_when_filtering()
    {
        var (hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var free = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), fee: 0);
        var fifteen = await SeedEventAsync(db, hostId, venue, Local(10, 6, 19), fee: 1500);
        var twentyFive = await SeedEventAsync(db, hostId, venue, Local(10, 7, 19), fee: 2500);
        var unknown = await SeedEventAsync(db, hostId, venue, Local(10, 8, 19), fee: null);

        Assert.Equal([free.PublicId], await SearchIdsAsync(Search("&maxFee=0")));
        Assert.Equal([free.PublicId, fifteen.PublicId], await SearchIdsAsync(Search("&maxFee=1500")));
        Assert.Equal([free.PublicId, fifteen.PublicId, twentyFive.PublicId], await SearchIdsAsync(Search("&maxFee=5000")));
        Assert.Equal([free.PublicId, fifteen.PublicId, twentyFive.PublicId, unknown.PublicId], await SearchIdsAsync(Search()));
    }

    [Fact]
    public async Task Search_excludes_drafts_hidden_archived_and_ended_events()
    {
        var (hostId, venue) = await ArrangeAsync();
        var now = Now(Factory);
        await using var db = Postgis.CreateDbContext();
        var published = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19));
        await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), status: null);
        await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), status: "hidden", hiddenReason: "admin");
        await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), status: "archived", archivedAt: now);
        await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), status: "archived", hiddenReason: "reports", archivedAt: now);
        await SeedEventAsync(db, hostId, venue, new DateTime(2026, 10, 3, 6, 0, 0));

        Assert.Equal([published.PublicId], await SearchIdsAsync(Search(from: "2026-10-03T00:00:00Z")));
    }

    [Fact]
    public async Task Cancelled_events_stay_listed_until_they_end()
    {
        var (hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        // 07:00-08:30 Toronto = 11:00-12:30 UTC; the clock is 12:00 UTC.
        var cancelled = await SeedEventAsync(db, hostId, venue, new DateTime(2026, 10, 3, 7, 0, 0), status: "cancelled");

        var events = (await SearchAsync(Search())).GetProperty("events");
        Assert.Equal(cancelled.PublicId, Assert.Single(events.EnumerateArray()).GetProperty("publicId").GetString());
        Assert.Equal("cancelled", events[0].GetProperty("status").GetString());

        Factory.Clock.Advance(Duration.FromMinutes(31));
        Assert.Empty(await SearchIdsAsync(Search()));
    }

    [Fact]
    public async Task Search_returns_100_results_and_flags_truncation()
    {
        var (hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        await SeedManyAsync(db, hostId, venue, 100);

        var exact = await SearchAsync(Search());
        Assert.Equal(100, exact.GetProperty("events").GetArrayLength());
        Assert.False(exact.GetProperty("truncated").GetBoolean());

        await SeedManyAsync(db, hostId, venue, 1);
        var truncated = await SearchAsync(Search());
        Assert.Equal(100, truncated.GetProperty("events").GetArrayLength());
        Assert.True(truncated.GetProperty("truncated").GetBoolean());
    }

    [Theory]
    [InlineData("/v1/search?lat=43.661&lng=-79.38&r=25&from=2026-10-03T12:00:00Z&days=14")]
    [InlineData("/v1/search?lat=43.66&lng=-79.38&r=20&from=2026-10-03T12:00:00Z&days=14")]
    [InlineData("/v1/search?lat=43.66&lng=-79.38&r=25&from=2026-10-03T12:15:00Z&days=14")]
    [InlineData("/v1/search?lat=43.66&lng=-79.38&r=25&from=2026-10-03T12:00:00Z&days=14&maxFee=1200")]
    [InlineData("/v1/search?lat=43.66&lng=-79.38&r=25&from=2026-10-03T12:00:00Z&days=14&smin=8")]
    [InlineData("/v1/search?lat=43.66&lng=-79.38")]
    public async Task Non_canonical_search_is_rejected_with_400(string uri)
    {
        using var client = Factory.CreatePublicClient();

        using var response = await client.GetAsync(new Uri(uri, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_search", await ProblemCodeAsync(response));
        Assert.Equal("private, no-store", response.Headers.NonValidated["Cache-Control"].ToString());
    }

    [Fact]
    public async Task Search_response_is_publicly_cacheable_and_tagged()
    {
        using var client = Factory.CreatePublicClient();

        using var response = await client.GetAsync(Search(), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=0, s-maxage=60, stale-while-revalidate=30", response.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Equal(SearchEvents.CacheTag, response.Headers.NonValidated["Cache-Tag"].ToString());
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Search_plan_uses_the_time_and_geo_indexes()
    {
        var (hostId, _) = await ArrangeAsync();
        await using (var seed = Postgis.CreateDbContext())
        {
            for (var i = 0; i < 40; i++)
            {
                var venue = await SeedVenueAtAsync(seed, 40 + (i * 0.25), -100 + (i * 0.5));
                await SeedManyAsync(seed, hostId, venue, 25, firstDay: i % 300);
            }
        }

        await using var db = Postgis.CreateDbContext();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var analyze = new NpgsqlCommand("ANALYZE events; ANALYZE venues; SET LOCAL enable_seqscan = off;", connection, transaction))
        {
            await analyze.ExecuteNonQueryAsync(Ct);
        }

        var query = SearchQuery.TryParse(Query(Search().OriginalString), Factory.Clock.GetCurrentInstant(), out _)!;
        await using var explain = new NpgsqlCommand("EXPLAIN " + SearchEvents.Sql, connection, transaction);
        SearchEvents.AddParameters(explain, query, Factory.Clock.GetCurrentInstant());
        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                plan.Add(reader.GetString(0));
            }
        }

        var text = string.Join('\n', plan);
        Assert.Contains("ix_events_time_active", text, StringComparison.Ordinal);
        Assert.Contains("ix_venues_geo", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Event_detail_follows_the_public_visibility_rule()
    {
        var (hostId, venue) = await ArrangeAsync();
        var now = Now(Factory);
        await using var db = Postgis.CreateDbContext();
        var published = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19));
        var cancelled = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), status: "cancelled");
        var archived = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), status: "archived", archivedAt: now);
        var archivedHidden = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), status: "archived", hiddenReason: "admin", archivedAt: now);
        var hidden = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), status: "hidden", hiddenReason: "reports");
        var draft = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), status: null);
        using var client = Factory.CreatePublicClient();

        foreach (var visible in new[] { published, cancelled, archived })
        {
            using var response = await client.GetAsync(EventUri(visible.PublicId), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("public, max-age=0, s-maxage=60", response.Headers.NonValidated["Cache-Control"].ToString());
            Assert.Equal($"event-{visible.PublicId}", response.Headers.NonValidated["Cache-Tag"].ToString());
            Assert.Equal(visible == archived, response.Headers.Contains(GetPublicEvent.RobotsTagHeader));
        }

        foreach (var invisible in new[] { archivedHidden, hidden, draft })
        {
            using var response = await client.GetAsync(EventUri(invisible.PublicId), Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("event_not_found", await ProblemCodeAsync(response));
            Assert.Equal("public, max-age=0, s-maxage=60", response.Headers.NonValidated["Cache-Control"].ToString());
            Assert.Equal($"event-{invisible.PublicId}", response.Headers.NonValidated["Cache-Tag"].ToString());
        }

        using var archivedResponse = await client.GetAsync(EventUri(archived.PublicId), Ct);
        Assert.Equal("noindex", archivedResponse.Headers.GetValues(GetPublicEvent.RobotsTagHeader).Single());
    }

    [Fact]
    public async Task Event_detail_returns_public_fields_in_venue_time()
    {
        var (hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var evt = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), status: "cancelled");
        using var client = Factory.CreatePublicClient();

        using var response = await client.GetAsync(EventUri(evt.PublicId), Ct);
        var body = await ReadJsonAsync(response);

        Assert.Equal("cancelled", body.GetProperty("status").GetString());
        Assert.Equal("2026-10-05T19:00:00", body.GetProperty("startsLocal").GetString());
        Assert.Equal("2026-10-05T19:00:00-04:00", body.GetProperty("startsAt").GetString());
        Assert.Equal(CleanJoin, body.GetProperty("joinInstructions").GetString());
        Assert.Equal(43.66, body.GetProperty("venue").GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(-79.38, body.GetProperty("venue").GetProperty("longitude").GetDouble(), 6);
        Assert.False(body.TryGetProperty("hostId", out _));
        Assert.False(body.TryGetProperty("version", out _));
    }

    [Theory]
    [InlineData("ABCDEFGHIJ")]
    [InlineData("abc")]
    [InlineData("abcdefgh19")]
    public async Task Malformed_event_id_is_a_404(string publicId)
    {
        using var client = Factory.CreatePublicClient();

        using var response = await client.GetAsync(EventUri(publicId), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Cache-Tag"));
    }

    [Fact]
    public async Task Ip_location_reads_cloudflare_headers_and_is_not_cached()
    {
        using var client = Factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/geo/ip");
        request.Headers.Add(GetIpLocation.LatitudeHeader, "43.65107");
        request.Headers.Add(GetIpLocation.LongitudeHeader, "-79.347015");

        using var located = await client.SendAsync(request, Ct);
        using var unknown = await client.GetAsync(new Uri("/v1/geo/ip", UriKind.Relative), Ct);

        var body = await ReadJsonAsync(located);
        Assert.Equal(43.65m, body.GetProperty("latitude").GetDecimal());
        Assert.Equal(-79.35m, body.GetProperty("longitude").GetDecimal());
        Assert.Equal("private, no-store", located.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Equal(JsonValueKind.Null, (await ReadJsonAsync(unknown)).GetProperty("latitude").ValueKind);
    }

    [Fact]
    public async Task Sitemap_lists_canonical_urls_of_published_and_cancelled_events()
    {
        var (hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var published = await SeedEventAsync(db, hostId, venue, Local(10, 5, 19), title: "Sunday Night Skate");
        var cancelled = await SeedEventAsync(db, hostId, venue, Local(10, 6, 19), status: "cancelled", title: "Équipe Hockey");
        await SeedEventAsync(db, hostId, venue, Local(10, 7, 19), status: "archived", archivedAt: Now(Factory));
        await SeedEventAsync(db, hostId, venue, Local(10, 7, 19), status: null);
        using var client = Factory.CreatePublicClient();

        using var response = await client.GetAsync(new Uri("/v1/sitemap.xml", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=0, s-maxage=3600", response.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        var body = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.NotEqual(0xEF, body[0]);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", Encoding.UTF8.GetString(body), StringComparison.Ordinal);
        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var urls = XDocument.Parse(Encoding.UTF8.GetString(body)).Descendants(ns + "loc").Select(loc => loc.Value).ToList();
        Assert.Equal(
            [$"https://hockeyindex.com/e/{published.PublicId}/sunday-night-skate", $"https://hockeyindex.com/e/{cancelled.PublicId}/equipe-hockey"],
            urls);
    }

    [Fact]
    public async Task Public_responses_set_no_cookie_even_when_the_session_is_due_for_renewal()
    {
        var (hostId, venue) = await ArrangeAsync();
        await using (var db = Postgis.CreateDbContext())
        {
            await SeedEventAsync(db, hostId, venue, new DateTime(2026, 11, 1, 19, 0, 0));
        }

        using var host = Factory.CreateHostClient();
        await host.SignInWithPhoneAsync(Phone(2));
        Factory.Clock.Advance(Duration.FromDays(20));
        var search = Search(days: 30, from: "2026-10-23T12:00:00Z");
        using var anonymous = Factory.CreatePublicClient();

        foreach (var uri in new[] { search, new Uri("/v1/geo/ip", UriKind.Relative), new Uri("/v1/sitemap.xml", UriKind.Relative) })
        {
            using var withCookie = await host.GetAsync(uri, Ct);
            using var without = await anonymous.GetAsync(uri, Ct);
            Assert.Equal(HttpStatusCode.OK, withCookie.StatusCode);
            Assert.False(withCookie.Headers.Contains("Set-Cookie"), uri.OriginalString);
            Assert.Equal(await without.Content.ReadAsStringAsync(Ct), await withCookie.Content.ReadAsStringAsync(Ct));
        }

        using var renewed = await host.GetMeAsync();
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        Assert.True(renewed.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Public_endpoints_allow_any_origin_without_an_origin_header()
    {
        using var client = Factory.CreatePublicClient();

        foreach (var uri in new[] { Search(), EventUri("abcdefgh23"), new Uri("/v1/geo/ip", UriKind.Relative), new Uri("/v1/sitemap.xml", UriKind.Relative) })
        {
            using var response = await client.GetAsync(uri, Ct);
            Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
    }

    [Fact]
    public async Task Edge_key_requests_share_a_600_per_minute_bucket_and_invalid_keys_fall_back_to_per_ip()
    {
        var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Edge:Key"] = "edge-secret",
            ["RateLimiting:PublicReadPerMinute"] = "2",
        });
        using var edgeA = factory.CreatePublicClient("198.51.100.1");
        using var edgeB = factory.CreatePublicClient("198.51.100.2");
        edgeA.DefaultRequestHeaders.Add(EdgeKeyAuth.HeaderName, "edge-secret");
        edgeB.DefaultRequestHeaders.Add(EdgeKeyAuth.HeaderName, "edge-secret");
        using var invalid = factory.CreatePublicClient("198.51.100.3");
        invalid.DefaultRequestHeaders.Add(EdgeKeyAuth.HeaderName, "wrong-key");
        var geo = new Uri("/v1/geo/ip", UriKind.Relative);

        for (var i = 0; i < 600; i++)
        {
            using var response = await (i % 2 == 0 ? edgeA : edgeB).GetAsync(geo, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var overLimit = await edgeA.GetAsync(geo, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, overLimit.StatusCode);

        var invalidStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await invalid.GetAsync(geo, Ct);
            invalidStatuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], invalidStatuses);
    }

    private static Uri EventUri(string publicId) => new($"/v1/events/{publicId}", UriKind.Relative);

    private static DateTime Local(int month, int day, int hour) => new(2026, month, day, hour, 0, 0);

    private static QueryCollection Query(string uri) =>
        new(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(new Uri("https://x"), uri).Query));

    private async Task<(Guid HostId, Venue Venue)> ArrangeAsync()
    {
        using var client = Factory.CreateHostClient();
        var hostId = await client.SignInWithPhoneAsync(Phone(1));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(Factory));
        return (hostId, venue);
    }

    private async Task<JsonElement> SearchAsync(Uri uri)
    {
        using var client = Factory.CreatePublicClient();
        using var response = await client.GetAsync(uri, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private async Task<List<string>> SearchIdsAsync(Uri uri, string extraFilters = "")
    {
        var target = extraFilters.Length == 0 ? uri : new Uri(uri.OriginalString + extraFilters, UriKind.Relative);
        var body = await SearchAsync(target);
        return body.GetProperty("events").EnumerateArray().Select(evt => evt.GetProperty("publicId").GetString()!).ToList();
    }

    private async Task<Venue> SeedVenueAtAsync(AppDbContext db, double latitude, double longitude)
    {
        var now = Now(Factory);
        var venue = new Venue
        {
            PublicId = PublicId.New(),
            Name = $"Arena {latitude:0.00} {longitude:0.00}",
            NameNormalized = TextNormalizer.Normalize($"Arena {latitude:0.00} {longitude:0.00}"),
            AddressLine = "2 Test Street",
            City = "Elsewhere",
            Region = "ON",
            Country = "CA",
            Geo = VenueGeometry.Point(latitude, longitude),
            TimeZone = Toronto,
            Provider = VenueProviders.Manual,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Venues.Add(venue);
        await db.SaveChangesAsync(Ct);
        return venue;
    }

    /// <param name="status">Lifecycle state to force; null leaves a draft.</param>
    private async Task<HockeyEvent> SeedEventAsync(
        AppDbContext db,
        Guid hostId,
        Venue venue,
        DateTime startsLocal,
        DateTime? endsLocal = null,
        EventType type = EventType.Scrimmage,
        (int Min, int Max)? skill = null,
        int? fee = 1500,
        string? status = "published",
        string? hiddenReason = null,
        DateTimeOffset? archivedAt = null,
        string title = "Friday scrimmage")
    {
        var evt = NewEvent(hostId, venue, startsLocal, endsLocal ?? startsLocal.AddMinutes(90), type, skill ?? (2, 4), fee, title);
        db.Events.Add(evt);
        await db.SaveChangesAsync(Ct);
        if (status is not null)
        {
            var hiddenFrom = status == "hidden" ? "published" : null;
            await ForceStatusAsync(db, evt.Id, status, hiddenReason, hiddenFrom, archivedAt);
        }

        return evt;
    }

    private async Task SeedManyAsync(AppDbContext db, Guid hostId, Venue venue, int count, int firstDay = 0)
    {
        var events = Enumerable.Range(0, count)
            .Select(i => NewEvent(
                hostId, venue, new DateTime(2026, 10, 4, 6, 0, 0).AddDays(firstDay).AddMinutes(i * 10), null, EventType.Scrimmage, (2, 4), 1500, $"Skate {i}"))
            .ToList();
        db.Events.AddRange(events);
        await db.SaveChangesAsync(Ct);
        var ids = events.Select(evt => evt.Id).ToArray();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE events SET status = 'published', published_at = created_at WHERE id = ANY({ids})", Ct);
    }

    private HockeyEvent NewEvent(
        Guid hostId, Venue venue, DateTime startsLocal, DateTime? endsLocal, EventType type, (int Min, int Max) skill, int? fee, string title)
    {
        var now = Now(Factory);
        var end = endsLocal ?? startsLocal.AddMinutes(90);
        return new HockeyEvent
        {
            PublicId = PublicId.New(),
            HostId = hostId,
            VenueId = venue.Id,
            Type = type,
            Title = title,
            StartsLocal = startsLocal,
            EndsLocal = end,
            StartsAt = Times.ToInstant(venue.TimeZone, startsLocal).Instant!.Value,
            EndsAt = Times.ToInstant(venue.TimeZone, end).Instant!.Value,
            ScheduleText = type == EventType.Scrimmage ? null : "Tuesdays 21:00",
            SkillRange = new NpgsqlRange<int>(skill.Min, true, skill.Max, true),
            FeeCents = fee,
            Currency = "CAD",
            JoinInstructions = CleanJoin,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
