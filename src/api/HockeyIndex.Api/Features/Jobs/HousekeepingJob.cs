using HockeyIndex.Api.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HockeyIndex.Api.Features.Jobs;

/// <summary>Deletes <c>otp_log</c> older than 30 days, <c>login_tokens</c> older than 1 day, <c>provider_usage</c> older than 90 days and expired <c>url_verdicts</c>.</summary>
public sealed class HousekeepingJob(IServiceScopeFactory scopes, IOptions<JobOptions> options, IClock clock, ILogger<HousekeepingJob> logger, HockeyIndexMetrics metrics)
    : PeriodicJob(scopes, options, clock, logger, metrics)
{
    public const string JobName = "housekeeping";
    public static readonly TimeSpan OtpLogRetention = TimeSpan.FromDays(30);
    public static readonly TimeSpan LoginTokenRetention = TimeSpan.FromDays(1);
    public const int ProviderUsageRetentionDays = 90;

    public override string Name => JobName;

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override async Task<JobBatchResult> RunBatchAsync(JobBatchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var database = context.Db.Database;
        var otpCutoff = context.Now - OtpLogRetention;
        var tokenCutoff = context.Now - LoginTokenRetention;
        var usageCutoff = DateOnly.FromDateTime(context.Now.UtcDateTime).AddDays(-ProviderUsageRetentionDays);

        var rows = await database.ExecuteSqlAsync($"DELETE FROM otp_log WHERE created_at < {otpCutoff}", cancellationToken);
        rows += await database.ExecuteSqlAsync($"DELETE FROM login_tokens WHERE created_at < {tokenCutoff}", cancellationToken);
        rows += await database.ExecuteSqlAsync($"DELETE FROM provider_usage WHERE day < {usageCutoff}", cancellationToken);
        rows += await database.ExecuteSqlAsync($"DELETE FROM url_verdicts WHERE expires_at < {context.Now}", cancellationToken);
        return new JobBatchResult(rows);
    }
}
