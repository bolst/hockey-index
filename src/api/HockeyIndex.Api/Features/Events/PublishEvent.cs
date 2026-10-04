using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Persistence.Locks;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Features.Events;

public enum PublishOutcome
{
    Published,
    ScanUnavailable,
    Blocked,
    Changed,
    NotDraft,
    Ended,
    ActiveLimitReached,
    DailyLimitReached,
    HostInactive,
}

public sealed record PublishAttempt(PublishOutcome Outcome, LinkScanResult? Scan = null);

/// <summary>
/// Publishes a draft (AC-LC-3, AC-LC-9, AC-TS-1): link scan outside any transaction, then under <see cref="HostLock"/>
/// and the event row lock, re-check <c>xmin</c> and the scanned text, enforce the caps and transition.
/// </summary>
public sealed class PublishEvent(
    AppDbContext db,
    LinkScanService scanner,
    LinkScanRecorder recorder,
    EventTransitions transitions,
    EventResponder responder,
    IClock clock)
{
    public const int MaxActiveListings = 10;
    public const int MaxPublishesPer24Hours = 5;

    public async Task<IResult> HandleAsync(string publicId, Guid hostId, HttpResponse response, CancellationToken cancellationToken)
    {
        if (await EventRows.FindOwnedAsync(db, publicId, hostId, cancellationToken) is not { } snapshot)
        {
            return EventProblems.NotFound();
        }

        var attempt = await TryPublishAsync(snapshot, TransitionActor.Host(hostId), hostId, cancellationToken);
        switch (attempt.Outcome)
        {
            case PublishOutcome.Published:
                return TypedResults.Ok(await RespondAsync(snapshot.Id, response, cancellationToken));
            case PublishOutcome.ScanUnavailable:
                return await RequestPublishAsync(snapshot, response, cancellationToken);
            case PublishOutcome.Blocked:
                return EventProblems.Blocked(attempt.Scan!.BlockedUrls, attempt.Scan.TooManyUrls);
            case PublishOutcome.Changed:
                return EventProblems.Changed();
            case PublishOutcome.NotDraft:
                return EventProblems.NotDraft();
            case PublishOutcome.Ended:
                return EventProblems.Ended();
            case PublishOutcome.ActiveLimitReached:
                return EventProblems.ActiveLimitReached();
            case PublishOutcome.DailyLimitReached:
                return EventProblems.DailyLimitReached();
            default:
                return EventProblems.HostInactive();
        }
    }

    /// <param name="snapshot">Untracked; its <c>Version</c> and <c>JoinInstructions</c> are the values the scan covers.</param>
    /// <param name="scanHostId">Host charged for the scan; null for jobs (global budget only).</param>
    public async Task<PublishAttempt> TryPublishAsync(
        HockeyEvent snapshot, TransitionActor actor, Guid? scanHostId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Status != EventStatus.Draft)
        {
            return new(PublishOutcome.NotDraft);
        }

        if (snapshot.HasEnded(Now()))
        {
            return new(PublishOutcome.Ended);
        }

        var scan = await scanner.ScanAsync(snapshot.JoinInstructions, scanHostId, cancellationToken);
        await recorder.RecordAsync(snapshot.Id, scan, Now(), cancellationToken);
        return scan.Status switch
        {
            LinkScanStatus.Blocked => new(PublishOutcome.Blocked, scan),
            LinkScanStatus.Unavailable => new(PublishOutcome.ScanUnavailable, scan),
            _ => new(await PublishCleanAsync(snapshot, actor, cancellationToken), scan),
        };
    }

    public static Task<int> CountActiveListingsAsync(AppDbContext db, Guid hostId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Events.CountAsync(
            evt => evt.HostId == hostId && (evt.Status == EventStatus.Published || evt.Status == EventStatus.Cancelled) && evt.EndsAt > now,
            cancellationToken);
    }

    /// <summary>Actual draft-to-published transitions; pending requests and restores do not count.</summary>
    public static Task<int> CountPublishesLast24HoursAsync(AppDbContext db, Guid hostId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var since = now.AddHours(-24);
        return db.EventStatusChanges.CountAsync(
            change => change.HostId == hostId
                && change.FromStatus == EventStatus.Draft
                && change.ToStatus == EventStatus.Published
                && change.At > since,
            cancellationToken);
    }

    private async Task<PublishOutcome> PublishCleanAsync(HockeyEvent snapshot, TransitionActor actor, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await HostLock.AcquireAsync(db, snapshot.HostId, cancellationToken) != HostStatus.Active)
        {
            return PublishOutcome.HostInactive;
        }

        var evt = await EventRows.LockAsync(db, snapshot.Id, cancellationToken);
        if (evt is null || evt.Version != snapshot.Version || evt.JoinInstructions != snapshot.JoinInstructions)
        {
            return PublishOutcome.Changed;
        }

        if (evt.Status != EventStatus.Draft)
        {
            return PublishOutcome.NotDraft;
        }

        var now = Now();
        if (evt.HasEnded(now))
        {
            return PublishOutcome.Ended;
        }

        if (await CountActiveListingsAsync(db, evt.HostId, now, cancellationToken) >= MaxActiveListings)
        {
            return PublishOutcome.ActiveLimitReached;
        }

        if (await CountPublishesLast24HoursAsync(db, evt.HostId, now, cancellationToken) >= MaxPublishesPer24Hours)
        {
            return PublishOutcome.DailyLimitReached;
        }

        evt.PublishRequestedAt = null;
        evt.Notice = null;
        evt.LastScannedAt = now;
        transitions.Publish(evt, actor);
        await transitions.CommitAsync(transaction, cancellationToken);
        return PublishOutcome.Published;
    }

    private async Task<IResult> RequestPublishAsync(HockeyEvent snapshot, HttpResponse response, CancellationToken cancellationToken)
    {
        var now = Now();
        var parked = await db.Events
            .Where(evt => evt.Id == snapshot.Id && evt.Version == snapshot.Version && evt.Status == EventStatus.Draft)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(evt => evt.PublishRequestedAt, evt => evt.PublishRequestedAt ?? now)
                    .SetProperty(evt => evt.Notice, (EventNotice?)null)
                    .SetProperty(evt => evt.UpdatedAt, now),
                cancellationToken);

        return parked == 0
            ? EventProblems.Changed()
            : TypedResults.Accepted($"/v1/host/events/{snapshot.PublicId}", await RespondAsync(snapshot.Id, response, cancellationToken));
    }

    private async Task<EventResponse> RespondAsync(Guid eventId, HttpResponse response, CancellationToken cancellationToken)
    {
        var evt = await db.Events.AsNoTracking().SingleAsync(candidate => candidate.Id == eventId, cancellationToken);
        return await responder.ToResponseAsync(evt, response, cancellationToken);
    }

    private DateTimeOffset Now() => clock.GetCurrentInstant().ToDateTimeOffset();
}
