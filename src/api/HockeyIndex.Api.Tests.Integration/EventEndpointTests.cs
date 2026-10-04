using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Caching;
using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.EventTestSupport;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class EventEndpointTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private static readonly Uri EventsUri = new("/v1/host/events", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unauthenticated_requests_are_rejected_with_401()
    {
        using var client = Factory.CreateHostClient();

        using var list = await client.GetAsync(EventsUri, Ct);
        using var create = await client.PostAsJsonAsync(EventsUri, ScrimmageBody("abcdefghij"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    [Fact]
    public async Task Creating_a_scrimmage_returns_the_draft_with_venue_times_and_an_etag()
    {
        var (_, client, _, venue) = await ArrangeAsync();

        using var response = await client.PostAsJsonAsync(EventsUri, ScrimmageBody(venue.PublicId), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var publicId = body.GetProperty("publicId").GetString()!;
        Assert.Matches(PublicId.Pattern, publicId);
        Assert.Equal($"/v1/host/events/{publicId}", response.Headers.Location!.OriginalString);
        Assert.Equal($"\"{body.GetProperty("version").GetUInt32()}\"", response.Headers.ETag!.Tag);
        Assert.Equal("draft", body.GetProperty("status").GetString());
        Assert.Equal("scrimmage", body.GetProperty("type").GetString());
        Assert.Equal("2026-10-10T19:00:00", body.GetProperty("startsLocal").GetString());
        Assert.Equal("2026-10-10T19:00:00-04:00", body.GetProperty("startsAt").GetString());
        Assert.Equal("2026-10-10T20:30:00-04:00", body.GetProperty("endsAt").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("startDate").ValueKind);
        Assert.Equal(2, body.GetProperty("skill").GetProperty("min").GetInt32());
        Assert.Equal(4, body.GetProperty("skill").GetProperty("max").GetInt32());
        Assert.Equal(1500, body.GetProperty("feeCents").GetInt32());
        Assert.Equal("CAD", body.GetProperty("currency").GetString());
        Assert.Equal(venue.PublicId, body.GetProperty("venue").GetProperty("publicId").GetString());
        Assert.Equal(Toronto, body.GetProperty("venue").GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task Creating_a_league_stores_whole_days_with_an_exclusive_end()
    {
        var (_, client, _, venue) = await ArrangeAsync();

        using var response = await client.PostAsJsonAsync(EventsUri, new
        {
            venueId = venue.PublicId,
            type = "league",
            title = "Tuesday beer league",
            startDate = "2026-11-03",
            endDate = "2027-03-30",
            scheduleText = "Tuesdays 21:30",
            skill = new { min = 1, max = 3 },
            feeCents = (int?)null,
            currency = "USD",
            joinInstructions = CleanJoin,
        }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("league", body.GetProperty("type").GetString());
        Assert.Equal("2026-11-03", body.GetProperty("startDate").GetString());
        Assert.Equal("2027-03-30", body.GetProperty("endDate").GetString());
        Assert.Equal("2027-03-31T00:00:00", body.GetProperty("endsLocal").GetString());
        Assert.Equal("2026-11-03T00:00:00-05:00", body.GetProperty("startsAt").GetString());
        Assert.Equal("2027-03-31T00:00:00-04:00", body.GetProperty("endsAt").GetString());
        Assert.Equal("Tuesdays 21:30", body.GetProperty("scheduleText").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("feeCents").ValueKind);
        Assert.Equal("USD", body.GetProperty("currency").GetString());
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(2, 8)]
    [InlineData(5, 4)]
    public async Task Skill_outside_zero_to_seven_is_rejected(int min, int max)
    {
        var (_, client, _, venue) = await ArrangeAsync();

        using var response = await client.PostAsJsonAsync(EventsUri, ScrimmageBody(venue.PublicId, skill: new { min, max }), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("invalid_event", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Scrimmage_shape_rejects_league_fields_and_negative_fees()
    {
        var (_, client, _, venue) = await ArrangeAsync();

        using var negativeFee = await client.PostAsJsonAsync(EventsUri, ScrimmageBody(venue.PublicId, feeCents: -1), Ct);
        using var leagueWithoutDates = await client.PostAsJsonAsync(EventsUri, new
        {
            venueId = venue.PublicId, type = "league", title = "League", skill = new { min = 0, max = 7 }, joinInstructions = CleanJoin,
        }, Ct);

        Assert.Equal("invalid_event", await ProblemCodeAsync(negativeFee));
        Assert.Equal("invalid_event", await ProblemCodeAsync(leagueWithoutDates));
    }

    [Fact]
    public async Task Skipped_local_time_is_rejected_and_ambiguous_time_resolves_to_the_earlier_instant()
    {
        var (_, client, _, venue) = await ArrangeAsync();

        using var skipped = await client.PostAsJsonAsync(
            EventsUri, ScrimmageBody(venue.PublicId, startsLocal: "2027-03-14T02:30:00", endsLocal: "2027-03-14T04:00:00"), Ct);
        using var ambiguous = await client.PostAsJsonAsync(
            EventsUri, ScrimmageBody(venue.PublicId, startsLocal: "2026-11-01T01:30:00", endsLocal: "2026-11-01T03:00:00"), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, skipped.StatusCode);
        var skippedBody = await ReadJsonAsync(skipped);
        Assert.Equal("local_time_skipped", skippedBody.GetProperty("code").GetString());
        Assert.Equal("startsLocal", skippedBody.GetProperty("field").GetString());

        Assert.Equal(HttpStatusCode.Created, ambiguous.StatusCode);
        var body = await ReadJsonAsync(ambiguous);
        Assert.Equal("2026-11-01T01:30:00-04:00", body.GetProperty("startsAt").GetString());
        Assert.Equal(["startsLocal"], body.GetProperty("resolvedAmbiguousTimes").EnumerateArray().Select(field => field.GetString()));
    }

    [Fact]
    public async Task Unknown_venue_is_rejected()
    {
        var (_, client, _, _) = await ArrangeAsync();

        using var response = await client.PostAsJsonAsync(EventsUri, ScrimmageBody(PublicId.New()), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("venue_not_found", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Updates_require_a_current_if_match()
    {
        var (_, client, _, venue) = await ArrangeAsync();
        using var created = await client.PostAsJsonAsync(EventsUri, ScrimmageBody(venue.PublicId), Ct);
        var publicId = (await ReadJsonAsync(created)).GetProperty("publicId").GetString()!;
        var etag = created.Headers.ETag!.Tag;

        using var missing = await client.PutWithETagAsync(publicId, ScrimmageBody(venue.PublicId, title: "Renamed"), etag: null);
        using var updated = await client.PutWithETagAsync(publicId, ScrimmageBody(venue.PublicId, title: "Renamed"), etag);
        using var stale = await client.PutWithETagAsync(publicId, ScrimmageBody(venue.PublicId, title: "Again"), etag);

        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal("if_match_required", await ProblemCodeAsync(missing));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Renamed", (await ReadJsonAsync(updated)).GetProperty("title").GetString());
        Assert.NotEqual(etag, updated.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("event_stale", await ProblemCodeAsync(stale));
    }

    [Fact]
    public async Task Other_hosts_events_are_not_found()
    {
        var (factory, _, _, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        using var other = factory.CreateHostClient("203.0.113.20");
        var otherHost = await other.SignInWithPhoneAsync(Phone(2));
        var foreign = await SeedDraftAsync(db, otherHost, venue, Now(factory));
        using var mine = factory.CreateHostClient();
        await mine.SignInWithPhoneAsync(Phone(1));

        using var get = await mine.GetAsync(new Uri($"/v1/host/events/{foreign.PublicId}", UriKind.Relative), Ct);
        using var publish = await mine.PostEventActionAsync(foreign.PublicId, "publish");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, publish.StatusCode);
    }

    [Fact]
    public async Task Publishing_a_clean_draft_records_the_change_and_purges_after_commit()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory));
        var queue = factory.Services.GetRequiredService<CachePurgeQueue>();
        var enqueuedBefore = queue.EnqueuedCount;

        using var response = await client.PostEventActionAsync(draft.PublicId, "publish");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("published", body.GetProperty("status").GetString());
        Assert.Equal("2026-10-03T12:00:00+00:00", body.GetProperty("publishedAt").GetString());
        var change = await db.EventStatusChanges.SingleAsync(row => row.EventId == draft.Id, Ct);
        Assert.Equal((EventStatus.Draft, EventStatus.Published, hostId), (change.FromStatus, change.ToStatus, change.ActorId!.Value));
        Assert.Equal(enqueuedBefore + 1, queue.EnqueuedCount);
        var purger = (FakeCachePurger)factory.Services.GetRequiredService<ICachePurger>();
        await WaitUntilAsync(() => purger.Purged.Contains(draft.PublicId));
        Assert.Equal(1, await db.LinkScans.CountAsync(scan => scan.EventId == draft.Id && scan.Verdict == "safe", Ct));
    }

    [Fact]
    public async Task Publishing_malicious_join_instructions_is_blocked_with_the_urls()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory), joinInstructions: $"Register: {MaliciousUrl}");

        using var response = await client.PostEventActionAsync(draft.PublicId, "publish");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("join_instructions_blocked", body.GetProperty("code").GetString());
        Assert.Equal([MaliciousUrl], body.GetProperty("blockedUrls").EnumerateArray().Select(url => url.GetString()));
        Assert.Equal(EventStatus.Draft, (await LoadAsync(db, draft.Id)).Status);
        Assert.Equal(1, await db.LinkScans.CountAsync(scan => scan.EventId == draft.Id && scan.Verdict == "malicious", Ct));
    }

    [Fact]
    public async Task Publishing_while_the_scanner_is_unavailable_parks_the_request()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory), joinInstructions: $"Join: {UnavailableUrl}");

        using var response = await client.PostEventActionAsync(draft.PublicId, "publish");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("draft", body.GetProperty("status").GetString());
        Assert.Equal("2026-10-03T12:00:00+00:00", body.GetProperty("publishRequestedAt").GetString());
        Assert.Equal(0, await db.EventStatusChanges.CountAsync(Ct));
    }

    [Fact]
    public async Task Cancel_and_delete_follow_the_lifecycle()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory));
        var other = await SeedDraftAsync(db, hostId, venue, Now(factory));

        using var cancelDraft = await client.PostEventActionAsync(draft.PublicId, "cancel");
        using var publish = await client.PostEventActionAsync(draft.PublicId, "publish");
        using var cancel = await client.PostEventActionAsync(draft.PublicId, "cancel");
        using var deletePublished = await client.DeleteAsync(new Uri($"/v1/host/events/{draft.PublicId}", UriKind.Relative), Ct);
        using var deleteDraft = await client.DeleteAsync(new Uri($"/v1/host/events/{other.PublicId}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Conflict, cancelDraft.StatusCode);
        Assert.Equal("invalid_transition", await ProblemCodeAsync(cancelDraft));
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("cancelled", (await ReadJsonAsync(cancel)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, deletePublished.StatusCode);
        Assert.Equal("event_not_draft", await ProblemCodeAsync(deletePublished));
        Assert.Equal(HttpStatusCode.NoContent, deleteDraft.StatusCode);
        Assert.False(await db.Events.AnyAsync(evt => evt.Id == other.Id, Ct));
    }

    [Fact]
    public async Task Duplicate_creates_a_draft_a_week_later_at_the_same_wall_time_across_dst()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var source = await SeedDraftAsync(db, hostId, venue, Now(factory), startsLocal: new DateTime(2026, 10, 28, 19, 0, 0));
        await ForceStatusAsync(db, source.Id, "published");

        using var response = await client.PostEventActionAsync(source.PublicId, "duplicate");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.NotEqual(source.PublicId, body.GetProperty("publicId").GetString());
        Assert.Equal("draft", body.GetProperty("status").GetString());
        Assert.Equal("2026-11-04T19:00:00", body.GetProperty("startsLocal").GetString());
        Assert.Equal("2026-11-04T19:00:00-05:00", body.GetProperty("startsAt").GetString());
        Assert.Equal(source.JoinInstructions, body.GetProperty("joinInstructions").GetString());
        Assert.Equal(2, await db.Events.CountAsync(Ct));
    }

    [Fact]
    public async Task Listing_returns_own_events_with_limits_and_filters_by_status()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var published = await SeedDraftAsync(db, hostId, venue, Now(factory));
        await SeedDraftAsync(db, hostId, venue, Now(factory));
        using (var publish = await client.PostEventActionAsync(published.PublicId, "publish"))
        {
            Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        }

        using var all = await client.GetAsync(EventsUri, Ct);
        using var drafts = await client.GetAsync(new Uri("/v1/host/events?status=draft", UriKind.Relative), Ct);
        using var invalid = await client.GetAsync(new Uri("/v1/host/events?status=bogus", UriKind.Relative), Ct);

        var body = await ReadJsonAsync(all);
        Assert.Equal(2, body.GetProperty("events").GetArrayLength());
        var limits = body.GetProperty("limits");
        Assert.Equal(1, limits.GetProperty("activeListings").GetInt32());
        Assert.Equal(10, limits.GetProperty("maxActiveListings").GetInt32());
        Assert.Equal(1, limits.GetProperty("publishesLast24Hours").GetInt32());
        Assert.Equal(5, limits.GetProperty("maxPublishesPer24Hours").GetInt32());
        Assert.Equal(1, (await ReadJsonAsync(drafts)).GetProperty("events").GetArrayLength());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
    }

    [Fact]
    public async Task Editing_published_join_instructions_with_malicious_links_is_rejected()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        var (publicId, etag) = await PublishedEventAsync(factory, client, hostId, venue);

        using var response = await client.PutWithETagAsync(publicId, ScrimmageBody(venue.PublicId, $"Now at {MaliciousUrl}"), etag);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("join_instructions_blocked", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Editing_published_join_instructions_with_the_scanner_down_keeps_the_old_text_live()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        var (publicId, etag) = await PublishedEventAsync(factory, client, hostId, venue);
        const string newText = "Moved to https://example.com/new-signup";
        factory.Services.GetRequiredService<ControllableReputationProvider>().IsDown = true;

        using var response = await client.PutWithETagAsync(publicId, ScrimmageBody(venue.PublicId, newText, title: "Edited"), etag);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(CleanJoin, body.GetProperty("joinInstructions").GetString());
        Assert.Equal(newText, body.GetProperty("pendingJoinInstructions").GetString());
        Assert.Equal("Edited", body.GetProperty("title").GetString());
        Assert.Equal("published", body.GetProperty("status").GetString());
    }

    private async Task<(ApiFactory Factory, HttpClient Client, Guid HostId, Venue Venue)> ArrangeAsync()
    {
        var factory = CreateFactory(configureServices: ControllableReputationProvider.Register);
        var client = factory.CreateHostClient();
        var hostId = await client.SignInWithPhoneAsync(Phone(1));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(factory));
        return (factory, client, hostId, venue);
    }

    private async Task<(string PublicId, string ETag)> PublishedEventAsync(ApiFactory factory, HttpClient client, Guid hostId, Venue venue)
    {
        await using var db = Postgis.CreateDbContext();
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory));
        using var publish = await client.PostEventActionAsync(draft.PublicId, "publish");
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        return (draft.PublicId, publish.Headers.ETag!.Tag);
    }

    private static DateTimeOffset Now(ApiFactory factory) => factory.Clock.GetCurrentInstant().ToDateTimeOffset();
}
