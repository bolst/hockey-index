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
/// Daily re-scan of live published and cancelled events (AC-TS-3). Shorteners are re-expanded because destinations change.
/// Each event is scanned in its own scope outside any transaction, then re-checked under its row lock: a malicious result hides
/// it with reason <c>link_scan</c> (purged after commit) only if the scanned join instructions are still live.
/// System scans charge the global Web Risk budget only.
/// </summary>
public sealed class LinkRescanJob(
    IServiceScopeFactory scopes, IOptions<JobOptions> options, IClock clock, ILogger<LinkRescanJob> logger, HockeyIndexMetrics metrics)
    : PeriodicJob(scopes, options, clock, logger, metrics)
{
    public const string JobName = "link_rescan";
    public const int BatchSize = 50;

    /// <summary>Events scanned more recently than this are skipped, so a daily run covers each event once.</summary>
    public static readonly TimeSpan RescanAfter = TimeSpan.FromHours(23);

    public override string Name => JobName;

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override async Task<JobBatchResult> RunBatchAsync(JobBatchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var now = context.Now;
        var scannedBefore = now - RescanAfter;
        var eventIds = await context.Db.Events.AsNoTracking()
            .Where(evt => (evt.Status == EventStatus.Published || evt.Status == EventStatus.Cancelled)
                && evt.EndsAt > now
                && (evt.LastScannedAt == null || evt.LastScannedAt < scannedBefore))
            .OrderBy(evt => evt.LastScannedAt.HasValue)
            .ThenBy(evt => evt.LastScannedAt)
            .ThenBy(evt => evt.Id)
            .Select(evt => evt.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var rows = 0;
        var anyUnavailable = false;
        foreach (var eventId in eventIds)
        {
            await using var scope = Scopes.CreateAsyncScope();
            var outcome = await RescanAsync(scope.ServiceProvider, eventId, cancellationToken);
            anyUnavailable |= outcome == LinkScanStatus.Unavailable;
            rows += outcome == LinkScanStatus.Blocked ? 1 : 0;
        }

        // Unavailable events keep their old last_scanned_at and would be picked again; stop and retry on the next run.
        return new JobBatchResult(rows, HasMore: eventIds.Count == BatchSize && !anyUnavailable);
    }

    /// <returns>The scan outcome; <see cref="LinkScanStatus.Blocked"/> means the event was hidden.</returns>
    private static async Task<LinkScanStatus?> RescanAsync(IServiceProvider services, Guid eventId, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var joinInstructions = await db.Events.AsNoTracking()
            .Where(evt => evt.Id == eventId)
            .Select(evt => evt.JoinInstructions)
            .SingleOrDefaultAsync(cancellationToken);
        if (joinInstructions is null)
        {
            return null;
        }

        var clock = services.GetRequiredService<IClock>();
        var scan = await services.GetRequiredService<LinkScanService>().ScanAsync(joinInstructions, hostId: null, cancellationToken);
        await services.GetRequiredService<LinkScanRecorder>().RecordAsync(eventId, scan, clock.GetCurrentInstant().ToDateTimeOffset(), cancellationToken);
        if (scan.Status == LinkScanStatus.Unavailable)
        {
            return scan.Status;
        }

        var transitions = services.GetRequiredService<EventTransitions>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var evt = await EventRows.LockAsync(db, eventId, cancellationToken);
        if (evt is null || evt.JoinInstructions != joinInstructions || evt.Status is not (EventStatus.Published or EventStatus.Cancelled))
        {
            return null;
        }

        if (scan.Status == LinkScanStatus.Blocked)
        {
            transitions.Hide(evt, HiddenReason.LinkScan, TransitionActor.System);
        }
        else
        {
            evt.LastScannedAt = clock.GetCurrentInstant().ToDateTimeOffset();
        }

        await transitions.CommitAsync(transaction, cancellationToken);
        return scan.Status;
    }
}
