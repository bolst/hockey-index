using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Admin;
using HockeyIndex.Api.Infrastructure.Caching;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;

namespace HockeyIndex.Api.Features.Events;

/// <summary>
/// The only writer of <c>status</c>, <c>hidden_reason</c> and <c>hidden_from_status</c> (enforced by
/// <c>EventStatusSingleWriterTests</c>). Each transition applies <see cref="EventStateMachine"/>, adds an
/// <c>event_status_changes</c> row, writes one <c>audit_log</c> row per audited admin action (same transaction) and, except for the sweep,
/// queues a cache purge that <see cref="CommitAsync"/> releases only after the transaction commits.
/// Callers MUST hold the event row lock (<c>FOR UPDATE</c>) when they call a transition.
/// </summary>
public sealed class EventTransitions(
    AppDbContext db, AuditRecorder audit, CachePurgeQueue purges, IClock clock, HockeyIndexMetrics metrics)
{
    public const int SweepBatchSize = 200;

    private readonly HashSet<string> purgeAfterCommit = new(StringComparer.Ordinal);
    private readonly List<string> autoHiddenBeforeCommit = [];
    private int publishedBeforeCommit;

    public void Publish(HockeyEvent evt, TransitionActor actor)
    {
        Apply(evt, EventTrigger.Publish, actor, hideReason: null, reason: null, purge: true);
    }

    public void Cancel(HockeyEvent evt, TransitionActor actor)
    {
        Apply(evt, EventTrigger.Cancel, actor, hideReason: null, reason: null, purge: true);
    }

    public void Hide(HockeyEvent evt, HiddenReason reason, TransitionActor actor)
    {
        Apply(evt, EventTrigger.Hide, actor, reason, SnakeCaseText.Of(reason), purge: true);
    }

    public void Restore(HockeyEvent evt, TransitionActor actor)
    {
        Apply(evt, EventTrigger.Restore, actor, hideReason: null, reason: null, purge: true);
    }

    public void ClearHiddenReason(HockeyEvent evt, TransitionActor actor)
    {
        Apply(evt, EventTrigger.ClearHiddenReason, actor, hideReason: null, reason: null, purge: true);
    }

    /// <summary>
    /// Archives up to <paramref name="batchSize"/> ended published, cancelled or hidden events, locked in id order.
    /// Queues no purge: an ended event's page changes only its "ended" banner and hidden stays 404, so the 60 s TTL suffices.
    /// MUST run inside a transaction; the caller saves and commits (wrap in <see cref="DeadlockRetry"/>).
    /// </summary>
    /// <returns>Rows archived.</returns>
    public async Task<int> ArchiveEndedBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        var now = Now();
        var ended = await db.Events
            .FromSql($"""
                SELECT e.*, e.xmin FROM events e
                WHERE e.status IN ('published','cancelled','hidden') AND e.ends_at < {now}
                ORDER BY e.id
                LIMIT {batchSize}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        foreach (var evt in ended)
        {
            Apply(evt, EventTrigger.Sweep, TransitionActor.System, hideReason: null, reason: null, purge: false);
        }

        return ended.Count;
    }

    /// <summary>Queues a purge for a field edit of a publicly visible event (drafts are never cached).</summary>
    public void PurgeAfterCommit(HockeyEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (evt.Status != EventStatus.Draft)
        {
            purgeAfterCommit.Add(evt.PublicId);
        }
    }

    /// <summary>Saves, commits <paramref name="transaction"/> when given, then releases queued purges.</summary>
    public async Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        purges.Enqueue(purgeAfterCommit);
        purgeAfterCommit.Clear();
        for (; publishedBeforeCommit > 0; publishedBeforeCommit--)
        {
            metrics.RecordEventPublished();
        }

        foreach (var reason in autoHiddenBeforeCommit)
        {
            metrics.RecordEventAutoHidden(reason);
        }

        autoHiddenBeforeCommit.Clear();
    }

    private void Apply(
        HockeyEvent evt,
        EventTrigger trigger,
        TransitionActor actor,
        HiddenReason? hideReason,
        string? reason,
        bool purge)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(actor);

        var now = Now();
        var from = evt.Status;
        var next = EventStateMachine.Apply(evt.Lifecycle, trigger, evt.HasEnded(now), hideReason);
        evt.ApplyLifecycle(next, now);

        db.EventStatusChanges.Add(new EventStatusChange
        {
            EventId = evt.Id,
            HostId = evt.HostId,
            FromStatus = from,
            ToStatus = next.Status,
            ActorId = actor.Id,
            Reason = reason,
            At = now,
        });

        if (actor is { Audit: { } adminAudit, Id: { } adminId })
        {
            var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["public_id"] = evt.PublicId,
                ["from"] = SnakeCaseText.Of(from),
                ["to"] = SnakeCaseText.Of(next.Status),
            };
            if (hideReason is { } hidden)
            {
                metadata["hidden_reason"] = SnakeCaseText.Of(hidden);
            }

            foreach (var (key, value) in adminAudit.Metadata ?? new Dictionary<string, object?>())
            {
                metadata[key] = value;
            }

            audit.Record(adminId, $"event.{SnakeCaseText.Of(trigger)}", AuditTargets.Event, evt.Id.ToString(), adminAudit.Reason, metadata);
        }

        if (trigger == EventTrigger.Publish)
        {
            publishedBeforeCommit++;
        }

        if (trigger == EventTrigger.Hide && actor.Id is null && hideReason is { } autoReason)
        {
            autoHiddenBeforeCommit.Add(SnakeCaseText.Of(autoReason));
        }

        if (purge)
        {
            purgeAfterCommit.Add(evt.PublicId);
        }
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Bulk transitions MUST run inside a transaction.");
        }
    }

    private DateTimeOffset Now() => clock.GetCurrentInstant().ToDateTimeOffset();
}
