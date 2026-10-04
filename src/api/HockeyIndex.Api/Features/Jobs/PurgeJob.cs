using HockeyIndex.Api.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HockeyIndex.Api.Features.Jobs;

/// <summary>Deletes events archived more than 12 months ago (AC-LC-6); scans and status changes cascade.</summary>
public sealed class PurgeJob(IServiceScopeFactory scopes, IOptions<JobOptions> options, IClock clock, ILogger<PurgeJob> logger, HockeyIndexMetrics metrics)
    : PeriodicJob(scopes, options, clock, logger, metrics)
{
    public const string JobName = "purge";
    public const int RetentionMonths = 12;
    public const int BatchSize = 500;

    public override string Name => JobName;

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override async Task<JobBatchResult> RunBatchAsync(JobBatchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var cutoff = context.Now.AddMonths(-RetentionMonths);
        var deleted = await context.Db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM events WHERE id IN (
                SELECT id FROM events
                WHERE status = 'archived' AND archived_at < {cutoff}
                ORDER BY id
                LIMIT {BatchSize}
                FOR UPDATE)
            """,
            cancellationToken);
        return new JobBatchResult(deleted, HasMore: deleted == BatchSize);
    }
}
