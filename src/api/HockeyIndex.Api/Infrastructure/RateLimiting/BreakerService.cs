using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Infrastructure.RateLimiting;

/// <summary>Durable circuit breakers for paid providers; state lives in <c>breakers</c> and survives restarts.</summary>
public sealed class BreakerService(AppDbContext db, IClock clock, HockeyIndexMetrics metrics)
{
    public async Task<bool> IsOpenAsync(string name, CancellationToken cancellationToken)
    {
        var now = Now();
        var openUntil = await db.Breakers.AsNoTracking()
            .Where(breaker => breaker.Name == name)
            .Select(breaker => breaker.OpenUntil)
            .SingleOrDefaultAsync(cancellationToken);
        metrics.SetBreaker(name, openUntil);
        return openUntil > now;
    }

    public async Task OpenAsync(string name, DateTimeOffset until, string reason, BreakerOpener openedBy, CancellationToken cancellationToken)
    {
        var openUntil = until.ToUniversalTime();
        var opener = BreakerOpenerValues.ToDatabase(openedBy);
        var now = Now();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO breakers (name, open_until, reason, opened_by, updated_at)
            VALUES ({name}, {openUntil}, {reason}, {opener}, {now})
            ON CONFLICT (name) DO UPDATE
            SET open_until = EXCLUDED.open_until,
                reason = EXCLUDED.reason,
                opened_by = EXCLUDED.opened_by,
                updated_at = EXCLUDED.updated_at
            """,
            cancellationToken);
        metrics.SetBreaker(name, openUntil);
    }

    public async Task CloseAsync(string name, CancellationToken cancellationToken)
    {
        var now = Now();
        await db.Breakers
            .Where(breaker => breaker.Name == name)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(breaker => breaker.OpenUntil, (DateTimeOffset?)null)
                    .SetProperty(breaker => breaker.UpdatedAt, now),
                cancellationToken);
        metrics.SetBreaker(name, null);
    }

    private DateTimeOffset Now() => clock.GetCurrentInstant().ToDateTimeOffset();
}
