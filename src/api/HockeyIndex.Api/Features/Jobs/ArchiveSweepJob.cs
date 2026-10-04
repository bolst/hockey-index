using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Features.Events;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HockeyIndex.Api.Features.Jobs;

/// <summary>Archives ended published, cancelled and hidden events (AC-LC-5). Queues no cache purge.</summary>
public sealed class ArchiveSweepJob(IServiceScopeFactory scopes, IOptions<JobOptions> options, IClock clock, ILogger<ArchiveSweepJob> logger, HockeyIndexMetrics metrics)
    : PeriodicJob(scopes, options, clock, logger, metrics)
{
    public const string JobName = "archive_sweep";

    public override string Name => JobName;

    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);

    protected override async Task<JobBatchResult> RunBatchAsync(JobBatchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var archived = await context.Services.GetRequiredService<EventTransitions>()
            .ArchiveEndedBatchAsync(EventTransitions.SweepBatchSize, cancellationToken);
        return new JobBatchResult(archived, HasMore: archived == EventTransitions.SweepBatchSize);
    }
}
