using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HockeyIndex.Api.Features.Jobs;

/// <summary>
/// Hides publicly visible events whose current links point at a blocklisted domain or one of its subdomains, matched against
/// <c>link_scans.final_host</c> and the URLs' own hosts (reason <c>domain_blocklist</c>, purged after commit). Runs daily and
/// right after an admin adds a domain. Targets are locked <c>ORDER BY id</c>; the batch retries on deadlock.
/// </summary>
public sealed class DomainBlocklistSweepJob(
    IServiceScopeFactory scopes, IOptions<JobOptions> options, IClock clock, ILogger<DomainBlocklistSweepJob> logger, HockeyIndexMetrics metrics)
    : PeriodicJob(scopes, options, clock, logger, metrics)
{
    public const string JobName = "domain_blocklist_sweep";
    public const int BatchSize = 200;

    public override string Name => JobName;

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override async Task<JobBatchResult> RunBatchAsync(JobBatchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var db = context.Db;
        var blockedDomains = await LinkScanService.LoadBlockedDomainsAsync(db, cancellationToken);
        if (blockedDomains.Count == 0)
        {
            return new JobBatchResult(0);
        }

        var candidateIds = await db.Database
            .SqlQuery<Guid>($"""
                SELECT DISTINCT e.id AS "Value"
                FROM events e
                JOIN link_scans s ON s.event_id = e.id
                JOIN blocklist b ON b.kind = 'domain'
                    AND (lower(s.final_host) = lower(b.value) OR lower(s.final_host) LIKE '%.' || lower(b.value))
                WHERE e.status IN ('published','cancelled') OR (e.status = 'archived' AND e.hidden_reason IS NULL)
                """)
            .ToListAsync(cancellationToken);
        if (candidateIds.Count == 0)
        {
            return new JobBatchResult(0);
        }

        var candidates = await db.Events.AsNoTracking().Where(evt => candidateIds.Contains(evt.Id)).ToListAsync(cancellationToken);
        var matches = await MatchingAsync(db, candidates, blockedDomains, cancellationToken);
        var targetIds = matches.Select(evt => evt.Id).Order().Take(BatchSize).ToArray();

        var locked = await db.Events
            .FromSql($"SELECT e.*, e.xmin FROM events e WHERE e.id = ANY({targetIds}) ORDER BY e.id FOR UPDATE")
            .ToListAsync(cancellationToken);
        var transitions = context.Services.GetRequiredService<EventTransitions>();
        var hidden = 0;
        foreach (var evt in await MatchingAsync(db, locked, blockedDomains, cancellationToken))
        {
            transitions.Hide(evt, HiddenReason.DomainBlocklist, TransitionActor.System);
            hidden++;
        }

        return new JobBatchResult(hidden, HasMore: matches.Count > BatchSize);
    }

    /// <summary>Visible events whose current join instructions link to a blocked host directly or after expansion.</summary>
    private static async Task<List<HockeyEvent>> MatchingAsync(
        AppDbContext db, IReadOnlyList<HockeyEvent> events, IReadOnlySet<string> blockedDomains, CancellationToken cancellationToken)
    {
        var visible = events.Where(EventVisibility.IsPublic).ToList();
        var ids = visible.Select(evt => evt.Id).ToList();
        var scans = (await db.LinkScans.AsNoTracking()
                .Where(scan => ids.Contains(scan.EventId))
                .Select(scan => new { scan.EventId, scan.Url, scan.FinalHost })
                .ToListAsync(cancellationToken))
            .ToLookup(scan => scan.EventId);

        return visible.Where(evt =>
        {
            var urls = UrlExtractor.Extract(evt.JoinInstructions).Urls.Select(extracted => extracted.Url).ToList();
            var current = urls.Select(url => url.AbsoluteUri).ToHashSet(StringComparer.Ordinal);
            return urls.Any(url => LinkScanService.IsBlockedHost(url.Host, blockedDomains))
                || scans[evt.Id].Any(scan => current.Contains(scan.Url) && LinkScanService.IsBlockedHost(scan.FinalHost, blockedDomains));
        }).ToList();
    }
}
