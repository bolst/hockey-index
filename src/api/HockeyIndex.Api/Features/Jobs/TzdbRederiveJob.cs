using HockeyIndex.Api.Infrastructure.Observability;
using System.Text.Json;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HockeyIndex.Api.Features.Jobs;

/// <summary>
/// When the tzdb version differs from <c>job_runs.metadata.tzdb</c>, recomputes <c>starts_at</c>/<c>ends_at</c> from the
/// venue wall times of non-archived events and stores the new version. Wall times that a rule change made nonexistent keep
/// their old instants and are logged.
/// </summary>
public sealed partial class TzdbRederiveJob(
    IServiceScopeFactory scopes,
    IOptions<JobOptions> options,
    IClock clock,
    IDateTimeZoneProvider zones,
    ILogger<TzdbRederiveJob> logger,
    HockeyIndexMetrics metrics)
    : PeriodicJob(scopes, options, clock, logger, metrics)
{
    public const string JobName = "tzdb_rederive";

    public override string Name => JobName;

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override bool RunsAtStartup => true;

    public static string MetadataFor(string tzdbVersion) => JsonSerializer.Serialize(new TzdbMetadata(tzdbVersion));

    protected override async Task<JobBatchResult> RunBatchAsync(JobBatchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var db = context.Db;
        var stored = await db.JobRuns.AsNoTracking()
            .Where(run => run.JobName == JobName)
            .Select(run => run.Metadata)
            .SingleOrDefaultAsync(cancellationToken);
        if (stored is not null && JsonSerializer.Deserialize<TzdbMetadata>(stored)?.Tzdb == zones.VersionId)
        {
            return new JobBatchResult(0);
        }

        var times = context.Services.GetRequiredService<VenueTimeConverter>();
        var rows = await db.Events
            .Where(evt => evt.Status != EventStatus.Archived)
            .Join(db.Venues, evt => evt.VenueId, venue => venue.Id, (evt, venue) => new { evt, venue.TimeZone })
            .ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var row in rows)
        {
            var starts = times.ToInstant(row.TimeZone, row.evt.StartsLocal);
            var ends = times.ToInstant(row.TimeZone, row.evt.EndsLocal);
            if (starts.Instant is not { } startsAt || ends.Instant is not { } endsAt || endsAt <= startsAt)
            {
                LogUnresolvable(logger, row.evt.PublicId, row.TimeZone);
                continue;
            }

            if (startsAt == row.evt.StartsAt && endsAt == row.evt.EndsAt)
            {
                continue;
            }

            LogRederived(logger, row.evt.PublicId, row.evt.StartsAt, startsAt);
            row.evt.StartsAt = startsAt;
            row.evt.EndsAt = endsAt;
            row.evt.UpdatedAt = context.Now;
            changed++;
        }

        return new JobBatchResult(changed, Metadata: MetadataFor(zones.VersionId));
    }

    private sealed record TzdbMetadata([property: System.Text.Json.Serialization.JsonPropertyName("tzdb")] string Tzdb);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event {PublicId} start moved from {OldStartsAt} to {NewStartsAt} after a tzdb update")]
    private static partial void LogRederived(ILogger logger, string publicId, DateTimeOffset oldStartsAt, DateTimeOffset newStartsAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event {PublicId} has a wall time that no longer exists in {TimeZone}; kept its instants")]
    private static partial void LogUnresolvable(ILogger logger, string publicId, string timeZone);
}
