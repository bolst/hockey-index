using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HockeyIndex.Api.Features.Jobs;

/// <summary>
/// Retries parked work while the job lock is held (AC-TS-1). Publish requests rerun <see cref="PublishEvent"/> with the caps;
/// pending join instructions P are scanned outside any transaction and applied only while the row still holds P.
/// Each event runs in its own scope so scans never run inside the lock transaction.
/// </summary>
public sealed class PendingScanJob(
    IServiceScopeFactory scopes, IOptions<JobOptions> options, IClock clock, ILogger<PendingScanJob> logger, HockeyIndexMetrics metrics)
    : PeriodicJob(scopes, options, clock, logger, metrics)
{
    public const string JobName = "pending_scan";
    public const int BatchSize = 50;
    public static readonly TimeSpan PublishRequestExpiry = TimeSpan.FromHours(24);

    public override string Name => JobName;

    protected override TimeSpan Interval => TimeSpan.FromMinutes(10);

    protected override async Task<JobBatchResult> RunBatchAsync(JobBatchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var publishRequestIds = await context.Db.Events.AsNoTracking()
            .Where(evt => evt.PublishRequestedAt != null)
            .OrderBy(evt => evt.PublishRequestedAt)
            .Select(evt => evt.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        var pendingJoinIds = await context.Db.Events.AsNoTracking()
            .Where(evt => evt.PendingJoinInstructions != null)
            .OrderBy(evt => evt.Id)
            .Select(evt => evt.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        Metrics.SetScanPendingEvents(
            await context.Db.Events.AsNoTracking().CountAsync(
                evt => evt.PublishRequestedAt != null || evt.PendingJoinInstructions != null, cancellationToken));

        var rows = 0;
        foreach (var eventId in publishRequestIds)
        {
            rows += await InScopeAsync(db => RetryPublishAsync(db, eventId, cancellationToken));
        }

        foreach (var eventId in pendingJoinIds)
        {
            rows += await InScopeAsync(db => PromotePendingJoinAsync(db, eventId, cancellationToken));
        }

        return new JobBatchResult(rows);

        async Task<int> InScopeAsync(Func<IServiceProvider, Task<int>> work)
        {
            await using var scope = Scopes.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }
    }

    private static async Task<int> RetryPublishAsync(IServiceProvider services, Guid eventId, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var snapshot = await db.Events.AsNoTracking().SingleOrDefaultAsync(evt => evt.Id == eventId, cancellationToken);
        if (snapshot?.PublishRequestedAt is not { } requestedAt)
        {
            return 0;
        }

        var attempt = await services.GetRequiredService<PublishEvent>()
            .TryPublishAsync(snapshot, TransitionActor.System, scanHostId: null, cancellationToken);
        if (attempt.Outcome == PublishOutcome.Published)
        {
            return 1;
        }

        var now = services.GetRequiredService<IClock>().GetCurrentInstant().ToDateTimeOffset();
        EventNotice? notice;
        switch (attempt.Outcome)
        {
            case PublishOutcome.Blocked:
                notice = EventNotice.PublishBlocked;
                break;
            case PublishOutcome.ActiveLimitReached or PublishOutcome.DailyLimitReached:
                notice = EventNotice.PublishLimitReached;
                break;
            case PublishOutcome.Ended:
            case PublishOutcome.ScanUnavailable when now - requestedAt >= PublishRequestExpiry:
                notice = EventNotice.PublishScanExpired;
                break;
            case PublishOutcome.HostInactive:
                notice = null;
                break;
            default:
                return 0;
        }

        return await db.Events
            .Where(evt => evt.Id == eventId && evt.Version == snapshot.Version)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(evt => evt.PublishRequestedAt, (DateTimeOffset?)null)
                    .SetProperty(evt => evt.Notice, notice)
                    .SetProperty(evt => evt.UpdatedAt, now),
                cancellationToken);
    }

    private static async Task<int> PromotePendingJoinAsync(IServiceProvider services, Guid eventId, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var pending = await db.Events.AsNoTracking()
            .Where(evt => evt.Id == eventId)
            .Select(evt => evt.PendingJoinInstructions)
            .SingleOrDefaultAsync(cancellationToken);
        if (pending is null)
        {
            return 0;
        }

        var clock = services.GetRequiredService<IClock>();
        var scan = await services.GetRequiredService<LinkScanService>().ScanAsync(pending, hostId: null, cancellationToken);
        await services.GetRequiredService<LinkScanRecorder>().RecordAsync(eventId, scan, clock.GetCurrentInstant().ToDateTimeOffset(), cancellationToken);
        if (scan.Status == LinkScanStatus.Unavailable)
        {
            return 0;
        }

        var transitions = services.GetRequiredService<EventTransitions>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var evt = await EventRows.LockAsync(db, eventId, cancellationToken);
        if (evt is null || evt.PendingJoinInstructions != pending)
        {
            return 0;
        }

        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        if (scan.Status == LinkScanStatus.Clean)
        {
            evt.JoinInstructions = pending;
            evt.LastScannedAt = now;
            transitions.PurgeAfterCommit(evt);
        }
        else
        {
            evt.Notice = EventNotice.JoinInstructionsBlocked;
        }

        evt.PendingJoinInstructions = null;
        evt.UpdatedAt = now;
        await transitions.CommitAsync(transaction, cancellationToken);
        return 1;
    }
}
