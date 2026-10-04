using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Persistence.Configurations;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Features.Admin;

/// <summary>Manual open/close of the provider circuit breakers (Principle 2).</summary>
public sealed class ManageBreakers(AppDbContext db, BreakerService breakers, AuditRecorder audit, IClock clock)
{
    public const int DefaultOpenMinutes = 60;
    public const int MaxOpenMinutes = 7 * 24 * 60;

    public static IReadOnlyList<string> Names { get; } = [BreakerNames.OtpSms, BreakerNames.Mapbox, BreakerNames.WebRisk];

    public async Task<IReadOnlyList<BreakerResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Breakers.AsNoTracking().ToDictionaryAsync(breaker => breaker.Name, cancellationToken);
        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        return
        [
            .. Names.Select(name => rows.TryGetValue(name, out var row)
                ? new BreakerResponse(
                    name, row.OpenUntil > now, row.OpenUntil, row.Reason,
                    row.OpenedBy is { } opener ? BreakerOpenerValues.ToDatabase(opener) : null)
                : new BreakerResponse(name, IsOpen: false, OpenUntil: null, Reason: null, OpenedBy: null)),
        ];
    }

    public async Task<IResult> ChangeAsync(string name, BreakerChangeRequest request, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Names.Contains(name, StringComparer.Ordinal))
        {
            return AdminProblems.BreakerNotFound();
        }

        var minutes = request.Minutes ?? DefaultOpenMinutes;
        if (request.Action is not ("open" or "close"))
        {
            return AdminProblems.Invalid("invalid_breaker_action", "Action must be open or close.");
        }

        if (minutes is < 1 or > MaxOpenMinutes)
        {
            return AdminProblems.Invalid("invalid_breaker_minutes", $"Minutes must be 1 to {MaxOpenMinutes}.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (request.Action == "open")
        {
            var until = clock.GetCurrentInstant().ToDateTimeOffset().AddMinutes(minutes);
            await breakers.OpenAsync(name, until, reason, BreakerOpener.Admin, cancellationToken);
            audit.Record(adminId, "breaker.open", AuditTargets.Breaker, name, reason, new { OpenUntil = until });
        }
        else
        {
            await breakers.CloseAsync(name, cancellationToken);
            audit.Record(adminId, "breaker.close", AuditTargets.Breaker, name, reason);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok((await ListAsync(cancellationToken)).Single(breaker => breaker.Name == name));
    }
}
