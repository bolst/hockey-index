using System.Net;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Features.Jobs;
using HockeyIndex.Api.Infrastructure.Caching;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Persistence.Locks;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.EventTestSupport;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class EventRaceTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private const int Racers = 20;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Twenty_concurrent_publishes_respect_the_daily_cap_of_five()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        var drafts = await SeedDraftsAsync(db, factory, hostId, venue, Racers);

        var responses = await Task.WhenAll(drafts.Select(draft => client.PostEventActionAsync(draft.PublicId, "publish")));

        Assert.Equal(PublishEvent.MaxPublishesPer24Hours, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        var rejected = responses.Where(response => response.StatusCode != HttpStatusCode.OK).ToList();
        Assert.Equal(Racers - PublishEvent.MaxPublishesPer24Hours, rejected.Count);
        foreach (var response in rejected)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal("daily_publish_limit_reached", await ProblemCodeAsync(response));
        }

        Assert.Equal(PublishEvent.MaxPublishesPer24Hours, await db.Events.CountAsync(evt => evt.Status == EventStatus.Published, Ct));
        Assert.Equal(PublishEvent.MaxPublishesPer24Hours, await db.EventStatusChanges.CountAsync(Ct));
        DisposeAll(responses);
    }

    [Fact]
    public async Task Twenty_concurrent_publishes_respect_the_active_cap_of_ten()
    {
        const int alreadyActive = 7;
        var (factory, client, hostId, venue) = await ArrangeAsync();
        await using var db = Postgis.CreateDbContext();
        foreach (var active in await SeedDraftsAsync(db, factory, hostId, venue, alreadyActive))
        {
            await ForceStatusAsync(db, active.Id, "published");
        }

        var drafts = await SeedDraftsAsync(db, factory, hostId, venue, Racers);

        var responses = await Task.WhenAll(drafts.Select(draft => client.PostEventActionAsync(draft.PublicId, "publish")));

        const int openSlots = PublishEvent.MaxActiveListings - alreadyActive;
        Assert.Equal(openSlots, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        foreach (var response in responses.Where(response => response.StatusCode != HttpStatusCode.OK))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal("active_limit_reached", await ProblemCodeAsync(response));
        }

        Assert.Equal(PublishEvent.MaxActiveListings, await db.Events.CountAsync(evt => evt.Status == EventStatus.Published, Ct));
        DisposeAll(responses);
    }

    [Fact]
    public async Task An_edit_committed_while_a_link_scan_is_in_flight_makes_the_scanned_edit_fail_with_409()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        var (publicId, etag) = await PublishedEventAsync(factory, client, hostId, venue);
        var reputation = factory.Services.GetRequiredService<ControllableReputationProvider>();

        var scannedEdit = client.PutWithETagAsync(publicId, ScrimmageBody(venue.PublicId, $"New link https://{ControllableReputationProvider.GatedHost}/a"), etag);
        await reputation.Entered.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        using var titleEdit = await client.PutWithETagAsync(publicId, ScrimmageBody(venue.PublicId, title: "Renamed during scan"), etag);
        reputation.Release();
        using var conflict = await scannedEdit;

        Assert.Equal(HttpStatusCode.OK, titleEdit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("event_changed", await ProblemCodeAsync(conflict));
        await using var db = Postgis.CreateDbContext();
        var evt = await db.Events.AsNoTracking().SingleAsync(candidate => candidate.PublicId == publicId, Ct);
        Assert.Equal("Renamed during scan", evt.Title);
        Assert.Equal(CleanJoin, evt.JoinInstructions);
    }

    [Fact]
    public async Task A_clean_edit_Q_during_the_scan_of_pending_P_wins_and_P_is_discarded()
    {
        var (factory, client, hostId, venue) = await ArrangeAsync();
        var (publicId, etag) = await PublishedEventAsync(factory, client, hostId, venue);
        var reputation = factory.Services.GetRequiredService<ControllableReputationProvider>();
        var pendingP = $"P https://{ControllableReputationProvider.GatedHost}/p";
        const string cleanQ = "Q https://example.com/q";

        reputation.IsDown = true;
        using var parked = await client.PutWithETagAsync(publicId, ScrimmageBody(venue.PublicId, pendingP), etag);
        Assert.Equal(HttpStatusCode.Accepted, parked.StatusCode);
        RecoverScanner(factory);

        var job = factory.Services.GetRequiredService<PendingScanJob>();
        var run = job.RunOnceAsync(Ct);
        await reputation.Entered.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        using var edit = await client.PutWithETagAsync(publicId, ScrimmageBody(venue.PublicId, cleanQ), parked.Headers.ETag!.Tag);
        reputation.Release();
        var result = await run;

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(JobRunStatus.Succeeded, result.Status);
        Assert.Equal(0, result.Rows);
        await using var db = Postgis.CreateDbContext();
        var evt = await db.Events.AsNoTracking().SingleAsync(candidate => candidate.PublicId == publicId, Ct);
        Assert.Equal(cleanQ, evt.JoinInstructions);
        Assert.Null(evt.PendingJoinInstructions);
        Assert.Null(evt.Notice);
    }

    [Fact]
    public async Task A_job_whose_lock_is_held_elsewhere_skips_without_touching_job_runs()
    {
        await using var connection = new NpgsqlConnection(Postgis.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using (var transaction = await connection.BeginTransactionAsync(Ct))
        {
            await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@class, hashtext(@name))", connection, transaction))
            {
                command.Parameters.AddWithValue("class", AdvisoryLockClasses.Jobs);
                command.Parameters.AddWithValue("name", ArchiveSweepJob.JobName);
                await command.ExecuteNonQueryAsync(Ct);
            }

            var skipped = await Factory.Services.GetRequiredService<ArchiveSweepJob>().RunOnceAsync(Ct);

            Assert.Equal(JobRunStatus.Skipped, skipped.Status);
            await using var db = Postgis.CreateDbContext();
            Assert.False(await db.JobRuns.AnyAsync(Ct));
        }

        var afterRelease = await Factory.Services.GetRequiredService<ArchiveSweepJob>().RunOnceAsync(Ct);
        Assert.Equal(JobRunStatus.Succeeded, afterRelease.Status);
    }

    [Fact]
    public async Task Two_job_instances_never_archive_the_same_event_twice()
    {
        const int endedEvents = 450;
        var factory = Factory;
        await using var db = Postgis.CreateDbContext();
        using var client = factory.CreateHostClient();
        var hostId = await client.SignInWithPhoneAsync(Phone(1));
        var venue = await SeedVenueAsync(db, Now(factory));
        await SeedDraftsAsync(db, factory, hostId, venue, endedEvents);
        await db.Database.ExecuteSqlAsync($"UPDATE events SET status = 'published', published_at = created_at", Ct);
        factory.Clock.Advance(Duration.FromDays(10));
        var queue = factory.Services.GetRequiredService<CachePurgeQueue>();
        var enqueuedBefore = queue.EnqueuedCount;
        var instances = Enumerable.Range(0, 2)
            .Select(_ => ActivatorUtilities.CreateInstance<ArchiveSweepJob>(factory.Services))
            .ToList();

        var results = await Task.WhenAll(instances.Select(job => job.RunOnceAsync(Ct)));

        Assert.All(results, result => Assert.NotEqual(JobRunStatus.Failed, result.Status));
        if (results.Any(result => result.Status == JobRunStatus.Skipped))
        {
            await factory.Services.GetRequiredService<ArchiveSweepJob>().RunOnceAsync(Ct);
        }

        Assert.Equal(endedEvents, await db.Events.CountAsync(evt => evt.Status == EventStatus.Archived, Ct));
        var archiveChanges = await db.EventStatusChanges
            .Where(change => change.ToStatus == EventStatus.Archived)
            .GroupBy(change => change.EventId)
            .Select(group => group.Count())
            .ToListAsync(Ct);
        Assert.Equal(endedEvents, archiveChanges.Count);
        Assert.All(archiveChanges, count => Assert.Equal(1, count));
        Assert.Equal(enqueuedBefore, queue.EnqueuedCount);
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

    private static async Task<List<HockeyEvent>> SeedDraftsAsync(
        AppDbContext db, ApiFactory factory, Guid hostId, Venue venue, int count)
    {
        var drafts = new List<HockeyEvent>(count);
        for (var i = 0; i < count; i++)
        {
            drafts.Add(await SeedDraftAsync(db, hostId, venue, Now(factory)));
        }

        return drafts;
    }

    private async Task<(string PublicId, string ETag)> PublishedEventAsync(ApiFactory factory, HttpClient client, Guid hostId, Venue venue)
    {
        await using var db = Postgis.CreateDbContext();
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory));
        using var publish = await client.PostEventActionAsync(draft.PublicId, "publish");
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        return (draft.PublicId, publish.Headers.ETag!.Tag);
    }

    private static void DisposeAll(IEnumerable<HttpResponseMessage> responses)
    {
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private static DateTimeOffset Now(ApiFactory factory) => factory.Clock.GetCurrentInstant().ToDateTimeOffset();
}
