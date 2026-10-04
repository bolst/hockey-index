using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using HockeyIndex.Api.Infrastructure.Text;
using HockeyIndex.Api.Integrations.WebRisk;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HockeyIndex.Api.Features.Safety;

/// <summary>
/// Scans the URLs in a piece of text: domain blocklist, shortener expansion, verdict cache, then Web Risk within the
/// <c>webrisk</c> breaker and budget. MUST be called outside any transaction: budget increments and cache writes commit
/// on their own, so no row lock is held while a provider call is in flight.
/// </summary>
public sealed partial class LinkScanService(
    AppDbContext db,
    IShortenerExpander expander,
    IUrlReputationProvider reputation,
    UrlVerdictCache verdicts,
    ProviderBudget budget,
    BreakerService breakers,
    HockeyIndexMetrics metrics,
    IClock clock,
    IOptions<LinkScanningOptions> options,
    ILogger<LinkScanService> logger)
{
    /// <summary>Web Risk returns no expiry for clean URLs; they are re-checked after this long.</summary>
    public static readonly TimeSpan SafeVerdictTtl = TimeSpan.FromHours(1);

    /// <summary>Used when Web Risk reports a threat without an <c>expireTime</c>.</summary>
    public static readonly TimeSpan MaliciousVerdictFallbackTtl = TimeSpan.FromHours(1);

    /// <param name="hostId">The host whose action triggered the scan; null for system jobs, which charge the global budget only.</param>
    public async Task<LinkScanResult> ScanAsync(string? text, Guid? hostId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException($"{nameof(LinkScanService)} MUST NOT run inside a transaction.");
        }

        var extraction = UrlExtractor.Extract(text);
        if (extraction.TooManyUrls || extraction.Rejected.Count > 0)
        {
            return new LinkScanResult(LinkScanStatus.Blocked, [], extraction.Rejected, extraction.TooManyUrls);
        }

        if (extraction.Urls.Count == 0)
        {
            return new LinkScanResult(LinkScanStatus.Clean, [], [], TooManyUrls: false);
        }

        var started = Stopwatch.GetTimestamp();
        var blockedDomains = await LoadBlockedDomainsAsync(cancellationToken);
        var expansions = await Task.WhenAll(extraction.Urls.Select(extracted =>
            IsBlocklisted(extracted.Url, blockedDomains)
                ? Task.FromResult<ShortenerExpansion?>(null)
                : ExpandAsync(extracted.Url, cancellationToken)));

        var links = new List<ScannedLink>(extraction.Urls.Count);
        for (var i = 0; i < extraction.Urls.Count; i++)
        {
            links.Add(await CheckAsync(extraction.Urls[i], expansions[i], blockedDomains, hostId, cancellationToken));
        }

        var status = links.Any(link => link.Verdict == LinkVerdict.Malicious) ? LinkScanStatus.Blocked
            : links.Any(link => link.Verdict == LinkVerdict.Unknown) ? LinkScanStatus.Unavailable
            : LinkScanStatus.Clean;
        metrics.RecordLinkScan(VerdictLabel(status), Stopwatch.GetElapsedTime(started));
        return new LinkScanResult(status, links, [], TooManyUrls: false);
    }

    private static string VerdictLabel(LinkScanStatus status) => status switch
    {
        LinkScanStatus.Blocked => "malicious",
        LinkScanStatus.Unavailable => "unknown",
        _ => "safe",
    };

    private async Task<ShortenerExpansion?> ExpandAsync(Uri url, CancellationToken cancellationToken) =>
        await expander.ExpandAsync(url, cancellationToken);

    private async Task<ScannedLink> CheckAsync(
        ExtractedUrl extracted, ShortenerExpansion? expansion, IReadOnlySet<string> blockedDomains, Guid? hostId, CancellationToken cancellationToken)
    {
        if (expansion is null)
        {
            return Link(extracted, extracted.Url, 0, LinkVerdict.Malicious, LinkVerdictSource.Blocklist, LinkScanReason.DomainBlocklisted);
        }

        var final = expansion.FinalUrl;
        var hops = expansion.RedirectHops;
        if (expansion.Chain.Any(url => IsBlocklisted(url, blockedDomains)))
        {
            return Link(extracted, final, hops, LinkVerdict.Malicious, LinkVerdictSource.Blocklist, LinkScanReason.DomainBlocklisted);
        }

        switch (expansion.Outcome)
        {
            case ExpansionOutcome.RedirectLimitExceeded:
                return Link(extracted, final, hops, LinkVerdict.Malicious, LinkVerdictSource.None, LinkScanReason.RedirectLimitExceeded);
            case ExpansionOutcome.DisallowedRedirect:
                return Link(extracted, final, hops, LinkVerdict.Malicious, LinkVerdictSource.None, LinkScanReason.DisallowedRedirect);
            case ExpansionOutcome.Failed:
                return Link(extracted, final, hops, LinkVerdict.Unknown, LinkVerdictSource.None, LinkScanReason.ExpansionFailed);
        }

        if (await verdicts.FindAsync(final, cancellationToken) is { } cached)
        {
            return cached.Verdict == UrlVerdictValues.Malicious
                ? Link(extracted, final, hops, LinkVerdict.Malicious, LinkVerdictSource.WebRisk, LinkScanReason.ThreatMatch, cached.ThreatTypes)
                : Link(extracted, final, hops, LinkVerdict.Safe, LinkVerdictSource.WebRisk, LinkScanReason.None);
        }

        if (await breakers.IsOpenAsync(BreakerNames.WebRisk, cancellationToken))
        {
            return Link(extracted, final, hops, LinkVerdict.Unknown, LinkVerdictSource.None, LinkScanReason.BreakerOpen);
        }

        if (!await budget.TryConsumeAsync(ProviderNames.WebRisk, hostId, cancellationToken))
        {
            return Link(extracted, final, hops, LinkVerdict.Unknown, LinkVerdictSource.None, LinkScanReason.BudgetExhausted);
        }

        var lookup = await reputation.LookupAsync(final, cancellationToken);
        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        switch (lookup.Status)
        {
            case UrlReputationStatus.Safe:
                await verdicts.StoreAsync(final, UrlVerdictValues.Safe, [], now + SafeVerdictTtl, cancellationToken);
                return Link(extracted, final, hops, LinkVerdict.Safe, LinkVerdictSource.WebRisk, LinkScanReason.None);

            case UrlReputationStatus.Malicious:
                var expiresAt = lookup.ExpiresAt ?? now + MaliciousVerdictFallbackTtl;
                if (expiresAt > now)
                {
                    await verdicts.StoreAsync(final, UrlVerdictValues.Malicious, lookup.ThreatTypes, expiresAt, cancellationToken);
                }

                return Link(extracted, final, hops, LinkVerdict.Malicious, LinkVerdictSource.WebRisk, LinkScanReason.ThreatMatch, lookup.ThreatTypes);

            default:
                if (lookup.IsProviderFault)
                {
                    await breakers.OpenAsync(
                        BreakerNames.WebRisk, now + options.Value.BreakerOpenFor, "provider_error", BreakerOpener.Auto, cancellationToken);
                    LogBreakerOpened(logger, options.Value.BreakerOpenFor);
                }

                return Link(extracted, final, hops, LinkVerdict.Unknown, LinkVerdictSource.None, LinkScanReason.ProviderError);
        }
    }

    private Task<IReadOnlySet<string>> LoadBlockedDomainsAsync(CancellationToken cancellationToken) =>
        LoadBlockedDomainsAsync(db, cancellationToken);

    /// <summary>Domain blocklist entries as ASCII hosts.</summary>
    public static async Task<IReadOnlySet<string>> LoadBlockedDomainsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var values = await db.Blocklist.AsNoTracking()
            .Where(entry => entry.Kind == BlocklistKind.Domain)
            .Select(entry => entry.Value)
            .ToListAsync(cancellationToken);
        return values.Select(UrlExtractor.ToAsciiHost).ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsBlocklisted(Uri url, IReadOnlySet<string> blockedDomains) => IsBlockedHost(url.Host, blockedDomains);

    /// <summary>A blocklisted domain also blocks every subdomain.</summary>
    public static bool IsBlockedHost(string host, IReadOnlySet<string> blockedDomains)
    {
        if (blockedDomains.Count == 0)
        {
            return false;
        }

        host = UrlExtractor.ToAsciiHost(host);
        while (true)
        {
            if (blockedDomains.Contains(host))
            {
                return true;
            }

            var dot = host.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0)
            {
                return false;
            }

            host = host[(dot + 1)..];
        }
    }

    private static ScannedLink Link(
        ExtractedUrl extracted,
        Uri final,
        int hops,
        LinkVerdict verdict,
        LinkVerdictSource source,
        LinkScanReason reason,
        IReadOnlyList<string>? threatTypes = null) =>
        new(extracted, final, hops, verdict, source, threatTypes ?? [], reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opened webrisk breaker for {Duration} after a provider fault")]
    private static partial void LogBreakerOpened(ILogger logger, TimeSpan duration);
}
