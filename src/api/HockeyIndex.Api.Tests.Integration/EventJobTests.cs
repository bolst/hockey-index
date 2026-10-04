using System.Net;
using System.Text.Json;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Jobs;
using HockeyIndex.Api.Infrastructure.Caching;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.EventTestSupport;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class EventJobTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Jobs_are_disabled_in_tests_so_nothing_runs_at_startup()
    {
        Assert.False(Factory.Services.GetRequiredService<IOptions<JobOptions>>().Value.Enabled);
        using var client = Factory.CreatePublicClient();
        using var health = await client.GetAsync(new Uri("/healthz", UriKind.Relative), Ct);

        await using var db = Postgis.CreateDbContext();
        Assert.False(await db.JobRuns.AnyAsync(Ct));
    }

    [Fact]
    public async Task Hidden_event_is_archived_after_it_ends_then_purged_after_twelve_months()
    {
        var (factory, _, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var evt = await SeedDraftAsync(db, hostId, venue, Now(factory));
        await ForceStatusAsync(db, evt.Id, "hidden", hiddenReason: "reports", hiddenFromStatus: "published");
        var sweep = factory.Services.GetRequiredService<ArchiveSweepJob>();
        var purge = factory.Services.GetRequiredService<PurgeJob>();
        var queue = factory.Services.GetRequiredService<CachePurgeQueue>();
        var purger = (FakeCachePurger)factory.Services.GetRequiredService<ICachePurger>();
        var enqueuedBefore = queue.EnqueuedCount;

        Assert.Equal(0, (await sweep.RunOnceAsync(Ct)).Rows);

        factory.Clock.Advance(Duration.FromDays(8));
        var archivedAt = Now(factory);
        var swept = await sweep.RunOnceAsync(Ct);

        Assert.Equal((JobRunStatus.Succeeded, 1L), (swept.Status, swept.Rows));
        var archived = await LoadAsync(db, evt.Id);
        Assert.Equal(EventStatus.Archived, archived.Status);
        Assert.Equal(HiddenReason.Reports, archived.HiddenReason);
        Assert.Null(archived.HiddenFromStatus);
        Assert.Equal(archivedAt, archived.ArchivedAt);
        var change = await db.EventStatusChanges.SingleAsync(row => row.EventId == evt.Id, Ct);
        Assert.Equal((EventStatus.Hidden, EventStatus.Archived), (change.FromStatus, change.ToStatus));
        Assert.Null(change.ActorId);
        Assert.Equal(enqueuedBefore, queue.EnqueuedCount);
        Assert.Empty(purger.Purged);

        factory.Clock.Advance(Duration.FromDays(330));
        Assert.Equal(0, (await purge.RunOnceAsync(Ct)).Rows);
        Assert.True(await db.Events.AnyAsync(row => row.Id == evt.Id, Ct));

        factory.Clock.Advance(Duration.FromDays(40));
        var purged = await purge.RunOnceAsync(Ct);

        Assert.Equal((JobRunStatus.Succeeded, 1L), (purged.Status, purged.Rows));
        Assert.False(await db.Events.AnyAsync(row => row.Id == evt.Id, Ct));
        Assert.False(await db.EventStatusChanges.AnyAsync(row => row.EventId == evt.Id, Ct));
        var runs = await db.JobRuns.AsNoTracking().ToDictionaryAsync(run => run.JobName, Ct);
        Assert.Equal(1, runs[ArchiveSweepJob.JobName].RowsAffected);
        Assert.Equal(1, runs[PurgeJob.JobName].RowsAffected);
        Assert.Null(runs[PurgeJob.JobName].LastError);
    }

    [Fact]
    public async Task Purge_keeps_events_that_are_not_archived()
    {
        var (factory, _, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory));
        factory.Clock.Advance(Duration.FromDays(800));

        var result = await factory.Services.GetRequiredService<PurgeJob>().RunOnceAsync(Ct);

        Assert.Equal(0, result.Rows);
        Assert.True(await db.Events.AnyAsync(row => row.Id == draft.Id, Ct));
    }

    [Fact]
    public async Task Parked_publish_is_published_by_the_pending_scan_job_once_the_scanner_recovers()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory));
        factory.Services.GetRequiredService<ControllableReputationProvider>().IsDown = true;
        using (var parked = await client.PostEventActionAsync(draft.PublicId, "publish"))
        {
            Assert.Equal(HttpStatusCode.Accepted, parked.StatusCode);
        }

        var job = factory.Services.GetRequiredService<PendingScanJob>();
        Assert.Equal(0, (await job.RunOnceAsync(Ct)).Rows);
        RecoverScanner(factory);
        var result = await job.RunOnceAsync(Ct);

        Assert.Equal(1, result.Rows);
        var evt = await LoadAsync(db, draft.Id);
        Assert.Equal(EventStatus.Published, evt.Status);
        Assert.Null(evt.PublishRequestedAt);
        var change = await db.EventStatusChanges.SingleAsync(row => row.EventId == draft.Id, Ct);
        Assert.Null(change.ActorId);
        var purger = (FakeCachePurger)factory.Services.GetRequiredService<ICachePurger>();
        await WaitUntilAsync(() => purger.Purged.Contains(draft.PublicId));
    }

    [Fact]
    public async Task Parked_publish_expires_after_twenty_four_hours_without_a_scan()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory));
        factory.Services.GetRequiredService<ControllableReputationProvider>().IsDown = true;
        using (var parked = await client.PostEventActionAsync(draft.PublicId, "publish"))
        {
            Assert.Equal(HttpStatusCode.Accepted, parked.StatusCode);
        }

        factory.Clock.Advance(Duration.FromHours(24));
        await factory.Services.GetRequiredService<PendingScanJob>().RunOnceAsync(Ct);

        var evt = await LoadAsync(db, draft.Id);
        Assert.Equal(EventStatus.Draft, evt.Status);
        Assert.Null(evt.PublishRequestedAt);
        Assert.Equal(EventNotice.PublishScanExpired, evt.Notice);
    }

    [Fact]
    public async Task Pending_join_instructions_go_live_when_clean_and_are_dropped_when_malicious()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var clean = await PublishAsync(db, factory, client, hostId, venue);
        var malicious = await PublishAsync(db, factory, client, hostId, venue);
        const string cleanText = "New signup https://example.com/new";
        var maliciousText = $"New signup {MaliciousUrl}";
        factory.Services.GetRequiredService<ControllableReputationProvider>().IsDown = true;
        using (var first = await client.PutWithETagAsync(clean.PublicId, ScrimmageBody(venue.PublicId, cleanText), clean.ETag))
        using (var second = await client.PutWithETagAsync(malicious.PublicId, ScrimmageBody(venue.PublicId, maliciousText), malicious.ETag))
        {
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        }

        RecoverScanner(factory);
        var queue = factory.Services.GetRequiredService<CachePurgeQueue>();
        var enqueuedBefore = queue.EnqueuedCount;
        var result = await factory.Services.GetRequiredService<PendingScanJob>().RunOnceAsync(Ct);

        Assert.Equal(2, result.Rows);
        var promoted = await LoadAsync(db, clean.Id);
        Assert.Equal(cleanText, promoted.JoinInstructions);
        Assert.Null(promoted.PendingJoinInstructions);
        var dropped = await LoadAsync(db, malicious.Id);
        Assert.Equal(CleanJoin, dropped.JoinInstructions);
        Assert.Null(dropped.PendingJoinInstructions);
        Assert.Equal(EventNotice.JoinInstructionsBlocked, dropped.Notice);
        Assert.Equal(enqueuedBefore + 1, queue.EnqueuedCount);
    }

    [Fact]
    public async Task Housekeeping_deletes_expired_verdicts_only()
    {
        await using var db = Postgis.CreateDbContext();
        var now = Now(Factory);
        db.UrlVerdicts.AddRange(
            new UrlVerdict { UrlHash = [1], Verdict = UrlVerdictValues.Safe, ExpiresAt = now.AddMinutes(-1), CreatedAt = now.AddHours(-2) },
            new UrlVerdict { UrlHash = [2], Verdict = UrlVerdictValues.Safe, ExpiresAt = now.AddMinutes(30), CreatedAt = now });
        await db.SaveChangesAsync(Ct);

        var result = await Factory.Services.GetRequiredService<HousekeepingJob>().RunOnceAsync(Ct);

        Assert.Equal(JobRunStatus.Succeeded, result.Status);
        var remaining = await db.UrlVerdicts.AsNoTracking().Select(verdict => verdict.UrlHash).ToListAsync(Ct);
        Assert.Equal([2], Assert.Single(remaining));
    }

    [Fact]
    public async Task Tzdb_rederive_fixes_drifted_instants_and_records_the_version()
    {
        var (factory, _, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var evt = await SeedDraftAsync(db, hostId, venue, Now(factory));
        await db.Database.ExecuteSqlAsync(
            $"UPDATE events SET starts_at = starts_at + interval '1 hour', ends_at = ends_at + interval '1 hour' WHERE id = {evt.Id}", Ct);
        var job = factory.Services.GetRequiredService<TzdbRederiveJob>();
        var version = factory.Services.GetRequiredService<IDateTimeZoneProvider>().VersionId;

        var first = await job.RunOnceAsync(Ct);
        var second = await job.RunOnceAsync(Ct);

        Assert.Equal((JobRunStatus.Succeeded, 1L), (first.Status, first.Rows));
        Assert.Equal((JobRunStatus.Succeeded, 0L), (second.Status, second.Rows));
        Assert.Equal(evt.StartsAt, (await LoadAsync(db, evt.Id)).StartsAt);
        var metadata = await db.JobRuns.AsNoTracking()
            .Where(run => run.JobName == TzdbRederiveJob.JobName).Select(run => run.Metadata).SingleAsync(Ct);
        using var document = JsonDocument.Parse(metadata!);
        Assert.Equal(version, document.RootElement.GetProperty("tzdb").GetString());
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

    private static async Task<(Guid Id, string PublicId, string ETag)> PublishAsync(
        AppDbContext db, ApiFactory factory, HttpClient client, Guid hostId, Venue venue)
    {
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory));
        using var publish = await client.PostEventActionAsync(draft.PublicId, "publish");
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        return (draft.Id, draft.PublicId, publish.Headers.ETag!.Tag);
    }

    private static DateTimeOffset Now(ApiFactory factory) => factory.Clock.GetCurrentInstant().ToDateTimeOffset();
}
