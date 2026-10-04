using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Persistence.Locks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;

namespace HockeyIndex.Api.Features.Admin;

/// <summary>
/// Admin queue, hide, restore and clear-hidden-reason (AC-TS-3..6). Every state change goes through <see cref="EventTransitions"/>
/// with an admin actor, which writes the <c>audit_log</c> row in the same transaction.
/// </summary>
public sealed class ModerateEvents(AppDbContext db, EventTransitions transitions, IClock clock)
{
    public const int QueueLimit = 200;
    public const int ReportsPerQueueItem = 20;

    /// <summary>Hidden events and visible events with unreviewed reports, most recent activity first.</summary>
    public async Task<IReadOnlyList<QueueItem>> QueueAsync(CancellationToken cancellationToken)
    {
        var reported = db.Reports.Where(report => report.ReviewedAt == null).Select(report => report.EventId);
        var events = await db.Events.AsNoTracking()
            .Where(evt => evt.Status == EventStatus.Hidden || reported.Contains(evt.Id))
            .OrderByDescending(evt => evt.UpdatedAt)
            .ThenBy(evt => evt.Id)
            .Take(QueueLimit)
            .ToListAsync(cancellationToken);

        var ids = events.Select(evt => evt.Id).ToList();
        var reports = (await db.Reports.AsNoTracking()
                .Where(report => ids.Contains(report.EventId) && report.ReviewedAt == null)
                .OrderByDescending(report => report.CreatedAt)
                .ToListAsync(cancellationToken))
            .ToLookup(report => report.EventId);

        return
        [
            .. events.Select(evt => new QueueItem(
                evt.PublicId,
                evt.Title,
                SnakeCaseText.Of(evt.Status),
                evt.HiddenReason is { } reason ? SnakeCaseText.Of(reason) : null,
                evt.HostId,
                evt.StartsAt,
                evt.EndsAt,
                reports[evt.Id].Count(),
                [.. reports[evt.Id].Take(ReportsPerQueueItem).Select(report => new ReportSummary(SnakeCaseText.Of(report.Reason), report.Details, report.CreatedAt))])),
        ];
    }

    public async Task<IResult> HideAsync(string publicId, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await LockByPublicIdAsync(publicId, cancellationToken) is not { } evt)
        {
            return EventProblems.NotFound();
        }

        if (evt.Status is not (EventStatus.Published or EventStatus.Cancelled or EventStatus.Archived))
        {
            return EventProblems.InvalidTransition();
        }

        transitions.Hide(evt, HiddenReason.Admin, TransitionActor.Admin(adminId, reason));
        await MarkReportsReviewedAsync(evt.Id, cancellationToken);
        return await CommitAsync(transaction, evt, cancellationToken);
    }

    /// <summary>
    /// Takes <see cref="HostLock"/> (shared with publish and ban), refuses a banned host, and marks reports reviewed so a new
    /// auto-hide needs three new reporters. MAY exceed the active cap; the audit row then records <c>cap_override</c>.
    /// </summary>
    public async Task<IResult> RestoreAsync(string publicId, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        if (!PublicId.IsValid(publicId)
            || await db.Events.AsNoTracking().SingleOrDefaultAsync(evt => evt.PublicId == publicId, cancellationToken) is not { } snapshot)
        {
            return EventProblems.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await HostLock.AcquireAsync(db, snapshot.HostId, cancellationToken) == HostStatus.Banned)
        {
            return AdminProblems.HostBanned();
        }

        var evt = await EventRows.LockAsync(db, snapshot.Id, cancellationToken);
        if (evt is null)
        {
            return EventProblems.NotFound();
        }

        if (evt.Status != EventStatus.Hidden)
        {
            return EventProblems.InvalidTransition();
        }

        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        Dictionary<string, object?>? metadata = null;
        var becomesActive = !evt.HasEnded(now) && evt.HiddenFromStatus is EventStatus.Published or EventStatus.Cancelled;
        if (becomesActive)
        {
            var activeBefore = await PublishEvent.CountActiveListingsAsync(db, evt.HostId, now, cancellationToken);
            if (activeBefore >= PublishEvent.MaxActiveListings)
            {
                metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["cap_override"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["active_before"] = activeBefore,
                        ["cap"] = PublishEvent.MaxActiveListings,
                    },
                };
            }
        }

        transitions.Restore(evt, TransitionActor.Admin(adminId, reason, metadata));
        await MarkReportsReviewedAsync(evt.Id, cancellationToken);
        return await CommitAsync(transaction, evt, cancellationToken);
    }

    /// <summary>Archived with a hidden reason → archived and publicly readable again (read-only, noindex).</summary>
    public async Task<IResult> ClearHiddenReasonAsync(string publicId, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await LockByPublicIdAsync(publicId, cancellationToken) is not { } evt)
        {
            return EventProblems.NotFound();
        }

        if (evt.Status != EventStatus.Archived || evt.HiddenReason is null)
        {
            return EventProblems.InvalidTransition();
        }

        transitions.ClearHiddenReason(evt, TransitionActor.Admin(adminId, reason));
        await MarkReportsReviewedAsync(evt.Id, cancellationToken);
        return await CommitAsync(transaction, evt, cancellationToken);
    }

    private async Task<HockeyEvent?> LockByPublicIdAsync(string publicId, CancellationToken cancellationToken)
    {
        if (!PublicId.IsValid(publicId))
        {
            return null;
        }

        var eventId = await db.Events.AsNoTracking()
            .Where(evt => evt.PublicId == publicId)
            .Select(evt => (Guid?)evt.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return eventId is { } id ? await EventRows.LockAsync(db, id, cancellationToken) : null;
    }

    private async Task MarkReportsReviewedAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        await db.Reports
            .Where(report => report.EventId == eventId && report.ReviewedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(report => report.ReviewedAt, now), cancellationToken);
    }

    private async Task<IResult> CommitAsync(IDbContextTransaction transaction, HockeyEvent evt, CancellationToken cancellationToken)
    {
        await transitions.CommitAsync(transaction, cancellationToken);
        return TypedResults.Ok(new AdminEventResponse(AdminEventSummary.From(evt)));
    }
}
