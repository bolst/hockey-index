using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Persistence.Locks;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HockeyIndex.Api.Features.Jobs;

public sealed class JobOptions
{
    public const string SectionName = "Jobs";

    /// <summary>False stops every timer; <see cref="PeriodicJob.RunOnceAsync"/> still works (tests drive jobs this way).</summary>
    public bool Enabled { get; set; } = true;
}

/// <param name="Rows">Rows changed by the batch.</param>
/// <param name="HasMore">Run another batch now.</param>
/// <param name="Metadata">JSON stored in <c>job_runs.metadata</c>; null keeps the stored value.</param>
public sealed record JobBatchResult(int Rows, bool HasMore = false, string? Metadata = null);

public enum JobRunStatus
{
    Succeeded,
    Skipped,
    Failed,
}

public sealed record JobRunResult(JobRunStatus Status, long Rows);

/// <param name="Services">The batch's scope; <see cref="Db"/> holds the open transaction with the job lock.</param>
public sealed record JobBatchContext(IServiceProvider Services, AppDbContext Db, DateTimeOffset Now);

/// <summary>
/// <see cref="BackgroundService"/> + <see cref="PeriodicTimer"/>. Each batch runs in its own scope and transaction guarded by
/// <c>pg_try_advisory_xact_lock(<see cref="AdvisoryLockClasses.Jobs"/>, hashtext(name))</c>; a busy lock skips the run, so
/// two instances never run the same batch. Deadlocked batches retry (<see cref="DeadlockRetry"/>). Outcomes go to <c>job_runs</c>.
/// </summary>
public abstract partial class PeriodicJob(IServiceScopeFactory scopes, IOptions<JobOptions> options, IClock clock, ILogger logger, HockeyIndexMetrics metrics)
    : BackgroundService
{
    public const int MaxBatchesPerRun = 100;

    protected IServiceScopeFactory Scopes { get; } = scopes;

    protected HockeyIndexMetrics Metrics { get; } = metrics;

    public abstract string Name { get; }

    protected abstract TimeSpan Interval { get; }

    protected virtual bool RunsAtStartup => false;

    public async Task<JobRunResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var startedAt = Now();
        var timer = Stopwatch.StartNew();
        Metrics.RegisterJobInterval(Name, Interval);
        long rows = 0;
        string? metadata = null;
        try
        {
            for (var batch = 0; batch < MaxBatchesPerRun; batch++)
            {
                var result = await DeadlockRetry.RunAsync(RunLockedBatchAsync, cancellationToken);
                if (result is null)
                {
                    LogSkipped(logger, Name);
                    Metrics.RecordJobRun(Name, "skipped", timer.Elapsed);
                    return new JobRunResult(JobRunStatus.Skipped, rows);
                }

                rows += result.Rows;
                metadata = result.Metadata ?? metadata;
                if (!result.HasMore)
                {
                    break;
                }
            }

            await RecordSuccessAsync(startedAt, rows, metadata, cancellationToken);
            LogSucceeded(logger, Name, rows);
            Metrics.RecordJobRun(Name, "succeeded", timer.Elapsed);
            return new JobRunResult(JobRunStatus.Succeeded, rows);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception, Name);
            await RecordFailureAsync(startedAt, exception, CancellationToken.None);
            Metrics.RecordJobRun(Name, "failed", timer.Elapsed);
            return new JobRunResult(JobRunStatus.Failed, rows);
        }
    }

    protected abstract Task<JobBatchResult> RunBatchAsync(JobBatchContext context, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        try
        {
            if (RunsAtStartup)
            {
                await RunOnceAsync(stoppingToken);
            }

            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<JobBatchResult?> RunLockedBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = Scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var locked = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({AdvisoryLockClasses.Jobs}, hashtext({Name})) AS \"Value\"")
            .SingleAsync(cancellationToken);
        if (!locked)
        {
            return null;
        }

        var result = await RunBatchAsync(new JobBatchContext(scope.ServiceProvider, db, Now()), cancellationToken);
        await scope.ServiceProvider.GetRequiredService<EventTransitions>().CommitAsync(transaction, cancellationToken);
        return result;
    }

    private async Task RecordSuccessAsync(DateTimeOffset startedAt, long rows, string? metadata, CancellationToken cancellationToken)
    {
        await using var scope = Scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var succeededAt = Now();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO job_runs (job_name, last_started_at, last_succeeded_at, last_error, rows_affected, metadata)
            VALUES ({Name}, {startedAt}, {succeededAt}, NULL, {rows}, CAST({metadata} AS jsonb))
            ON CONFLICT (job_name) DO UPDATE SET
                last_started_at = EXCLUDED.last_started_at,
                last_succeeded_at = EXCLUDED.last_succeeded_at,
                last_error = NULL,
                rows_affected = EXCLUDED.rows_affected,
                metadata = COALESCE(EXCLUDED.metadata, job_runs.metadata)
            """,
            cancellationToken);
    }

    private async Task RecordFailureAsync(DateTimeOffset startedAt, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = Scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var error = $"{exception.GetType().Name}: {exception.Message}";
            error = error.Length <= 2000 ? error : error[..2000];
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO job_runs (job_name, last_started_at, last_error)
                VALUES ({Name}, {startedAt}, {error})
                ON CONFLICT (job_name) DO UPDATE SET last_started_at = EXCLUDED.last_started_at, last_error = EXCLUDED.last_error
                """,
                cancellationToken);
        }
        catch (Exception recordException) when (recordException is not OperationCanceledException)
        {
            LogFailed(logger, recordException, Name);
        }
    }

    private DateTimeOffset Now() => clock.GetCurrentInstant().ToDateTimeOffset();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Job {Job} skipped: another instance holds its lock")]
    private static partial void LogSkipped(ILogger logger, string job);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {Job} succeeded, {Rows} rows")]
    private static partial void LogSucceeded(ILogger logger, string job, long rows);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Job} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string job);
}
