using HockeyIndex.Api.Infrastructure.Text;

namespace HockeyIndex.Api.Features.Safety;

public enum LinkScanStatus
{
    /// <summary>Every URL was checked and none is known bad.</summary>
    Clean,

    /// <summary>At least one URL is known bad or not allowed. Reject the save (422).</summary>
    Blocked,

    /// <summary>
    /// No URL is known bad, but at least one could not be checked (provider error, breaker open, budget exhausted,
    /// shortener unreachable). Never publish: park via <c>publish_requested_at</c> / <c>pending_join_instructions</c>.
    /// </summary>
    Unavailable,
}

/// <summary>Values match <c>link_scans.verdict</c>.</summary>
public enum LinkVerdict
{
    Safe,
    Malicious,
    Unknown,
}

/// <summary>Values match <c>link_scans.provider</c>.</summary>
public enum LinkVerdictSource
{
    None,
    WebRisk,
    Blocklist,
}

public enum LinkScanReason
{
    None,
    ThreatMatch,
    DomainBlocklisted,
    RedirectLimitExceeded,
    DisallowedRedirect,
    ExpansionFailed,
    BreakerOpen,
    BudgetExhausted,
    ProviderError,
}

/// <param name="Url">Canonical extracted URL.</param>
/// <param name="FinalUrl">Destination after shortener expansion (equals <paramref name="Url"/> when not shortened).</param>
public sealed record ScannedLink(
    ExtractedUrl Extracted,
    Uri FinalUrl,
    int RedirectHops,
    LinkVerdict Verdict,
    LinkVerdictSource Source,
    IReadOnlyList<string> ThreatTypes,
    LinkScanReason Reason)
{
    public Uri Url => Extracted.Url;
}

public sealed record LinkScanResult(
    LinkScanStatus Status,
    IReadOnlyList<ScannedLink> Links,
    IReadOnlyList<RejectedUrl> RejectedUrls,
    bool TooManyUrls)
{
    /// <summary>URLs as written in the text that caused <see cref="LinkScanStatus.Blocked"/>, for the error response.</summary>
    public IReadOnlyList<string> BlockedUrls =>
    [
        .. RejectedUrls.Select(rejected => rejected.Original),
        .. Links.Where(link => link.Verdict == LinkVerdict.Malicious).Select(link => link.Extracted.Original),
    ];
}
