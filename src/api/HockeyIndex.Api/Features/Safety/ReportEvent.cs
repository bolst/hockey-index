using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Auth;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Persistence.Locks;
using HockeyIndex.Api.Integrations.Turnstile;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Features.Safety;

/// <summary>
/// POST /v1/events/{publicId}/reports (AC-TS-4): Turnstile, then the per-reporter limit, then under the event row lock
/// require public visibility, insert (one per reporter) and hide with reason <c>reports</c> at
/// <see cref="AutoHideThreshold"/> unreviewed reporters. The reporter is the HMAC of the IPv4 address or IPv6 /64.
/// </summary>
public sealed class ReportEvent(
    AppDbContext db, ITurnstileVerifier turnstile, SecretHasher hasher, EventTransitions transitions, IClock clock,
    HockeyIndexMetrics metrics)
{
    public const int AutoHideThreshold = 3;
    public const int MaxReportsPerHour = 5;
    public const int MaxReportsPerDay = 20;

    public async Task<IResult> HandleAsync(string publicId, ReportRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(http);

        if (!PublicId.IsValid(publicId))
        {
            return EventProblems.NotFound();
        }

        if (!SnakeCaseText.TryParse<ReportReason>(request.Reason, out var reason))
        {
            return SafetyProblems.InvalidReport("Choose a reason.");
        }

        var details = string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim();
        if (details is { Length: > Report.DetailsMaxLength })
        {
            return SafetyProblems.InvalidReport($"Details must be at most {Report.DetailsMaxLength} characters.");
        }

        var clientIp = http.GetClientIp();
        if (!await turnstile.VerifyAsync(request.TurnstileToken, TurnstileActions.Report, clientIp.Address, cancellationToken))
        {
            return AuthProblems.TurnstileFailed();
        }

        var reporterHash = hasher.HashIpKey(clientIp.KeyHex);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ipKey = clientIp.KeyHex;
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockClasses.Reports}, hashtext({ipKey}))", cancellationToken);

        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        if (await IsOverLimitAsync(reporterHash, now, cancellationToken))
        {
            return SafetyProblems.ReportLimitReached();
        }

        var eventId = await db.Events.AsNoTracking()
            .Where(evt => evt.PublicId == publicId)
            .Select(evt => (Guid?)evt.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var evt = eventId is { } id ? await EventRows.LockAsync(db, id, cancellationToken) : null;
        if (evt is null || !EventVisibility.IsPublic(evt))
        {
            return EventProblems.NotFound();
        }

        var reasonText = SnakeCaseText.Of(reason);
        var reportId = Guid.CreateVersion7();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO reports (id, event_id, reporter_hash, reason, details, created_at)
            VALUES ({reportId}, {evt.Id}, {reporterHash}, {reasonText}, {details}, {now})
            ON CONFLICT (event_id, reporter_hash) DO NOTHING
            """,
            cancellationToken);

        var unreviewed = await db.Reports.CountAsync(report => report.EventId == evt.Id && report.ReviewedAt == null, cancellationToken);
        if (unreviewed >= AutoHideThreshold)
        {
            transitions.Hide(evt, HiddenReason.Reports, TransitionActor.System);
        }

        await transitions.CommitAsync(transaction, cancellationToken);
        metrics.RecordReport();
        return TypedResults.NoContent();
    }

    private async Task<bool> IsOverLimitAsync(byte[] reporterHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var dayAgo = now.AddDays(-1);
        var hourAgo = now.AddHours(-1);
        var recent = await db.Reports.AsNoTracking()
            .Where(report => report.ReporterHash == reporterHash && report.CreatedAt > dayAgo)
            .Select(report => report.CreatedAt)
            .ToListAsync(cancellationToken);
        return recent.Count >= MaxReportsPerDay || recent.Count(createdAt => createdAt > hourAgo) >= MaxReportsPerHour;
    }
}
