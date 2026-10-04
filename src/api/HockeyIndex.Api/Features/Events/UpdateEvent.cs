using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Infrastructure.Persistence;
using NodaTime;

namespace HockeyIndex.Api.Features.Events;

/// <summary>
/// Edits an event (AC-LC-7). Drafts store join instructions unscanned. On a published or cancelled event a changed text is
/// scanned outside the transaction: clean goes live, blocked is 422, unavailable parks it in
/// <c>pending_join_instructions</c> (202) while the last clean text stays live. Every clean write nulls the pending text.
/// </summary>
public sealed class UpdateEvent(
    AppDbContext db,
    EventValidator validator,
    LinkScanService scanner,
    LinkScanRecorder recorder,
    EventTransitions transitions,
    EventResponder responder,
    IClock clock)
{
    public async Task<IResult> HandleAsync(
        string publicId, EventRequest request, Guid hostId, HttpRequest httpRequest, HttpResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!EventETags.TryReadIfMatch(httpRequest, out var expectedVersion))
        {
            return EventProblems.IfMatchRequired();
        }

        if (await EventRows.FindOwnedAsync(db, publicId, hostId, cancellationToken) is not { } snapshot)
        {
            return EventProblems.NotFound();
        }

        if (snapshot.Version != expectedVersion)
        {
            return EventProblems.Stale();
        }

        if (!IsEditable(snapshot.Status))
        {
            return EventProblems.NotEditable();
        }

        var (validated, problem) = await validator.ValidateAsync(request, cancellationToken);
        if (validated is null)
        {
            return problem!;
        }

        var scanStatus = (LinkScanStatus?)null;
        if (snapshot.Status != EventStatus.Draft && validated.JoinInstructions != snapshot.JoinInstructions)
        {
            var scan = await scanner.ScanAsync(validated.JoinInstructions, hostId, cancellationToken);
            await recorder.RecordAsync(snapshot.Id, scan, Now(), cancellationToken);
            if (scan.Status == LinkScanStatus.Blocked)
            {
                return EventProblems.Blocked(scan.BlockedUrls, scan.TooManyUrls);
            }

            scanStatus = scan.Status;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var evt = await EventRows.LockAsync(db, snapshot.Id, cancellationToken);
        if (evt is null)
        {
            return EventProblems.NotFound();
        }

        if (evt.Version != snapshot.Version)
        {
            return scanStatus is null ? EventProblems.Stale() : EventProblems.Changed();
        }

        var now = Now();
        validated.ApplyTo(evt, now);
        switch (scanStatus)
        {
            case null when evt.Status == EventStatus.Draft:
                evt.JoinInstructions = validated.JoinInstructions;
                break;
            case null:
                evt.PendingJoinInstructions = null;
                break;
            case LinkScanStatus.Clean:
                evt.JoinInstructions = validated.JoinInstructions;
                evt.PendingJoinInstructions = null;
                evt.LastScannedAt = now;
                break;
            case LinkScanStatus.Unavailable:
                evt.PendingJoinInstructions = validated.JoinInstructions;
                break;
        }

        transitions.PurgeAfterCommit(evt);
        await transitions.CommitAsync(transaction, cancellationToken);

        var body = await responder.ToResponseAsync(evt, response, cancellationToken, validated.ResolvedAmbiguousTimes);
        return scanStatus == LinkScanStatus.Unavailable
            ? TypedResults.Accepted($"/v1/host/events/{evt.PublicId}", body)
            : TypedResults.Ok(body);
    }

    private static bool IsEditable(EventStatus status) => status is EventStatus.Draft or EventStatus.Published or EventStatus.Cancelled;

    private DateTimeOffset Now() => clock.GetCurrentInstant().ToDateTimeOffset();
}
