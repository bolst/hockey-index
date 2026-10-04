using System.Globalization;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Text;
using Microsoft.EntityFrameworkCore;

namespace HockeyIndex.Api.Features.Safety;

/// <summary>
/// GET /v1/out/check?u&amp;e (AC-TS-2, R12). <c>u</c> resolves only when it is one of the URLs in the current join instructions of
/// the publicly visible event <c>e</c> (or that URL's expanded destination) and the latest scan of that URL is <c>safe</c>.
/// Everything else is <c>unrecognized</c>, so <c>/out</c> never acts as an open redirect.
/// </summary>
public sealed class CheckOutboundLink(AppDbContext db)
{
    public async Task<OutboundLinkResponse> HandleAsync(string? u, string? e, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(u) || u.Length > 2048 || !PublicId.IsValid(e ?? "")
            || !UrlExtractor.TryNormalize(u.Trim(), out var requested, out _))
        {
            return Unrecognized();
        }

        var evt = await db.Events.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.PublicId == e, cancellationToken);
        if (evt is null || !EventVisibility.IsPublic(evt))
        {
            return Unrecognized();
        }

        var currentUrls = UrlExtractor.Extract(evt.JoinInstructions).Urls.Select(extracted => extracted.Url.AbsoluteUri).ToList();
        if (currentUrls.Count == 0)
        {
            return Unrecognized();
        }

        var scans = await db.LinkScans.AsNoTracking()
            .Where(scan => scan.EventId == evt.Id && currentUrls.Contains(scan.Url))
            .OrderByDescending(scan => scan.CheckedAt)
            .ThenByDescending(scan => scan.Id)
            .ToListAsync(cancellationToken);

        var target = requested.AbsoluteUri;
        var match = scans
            .GroupBy(scan => scan.Url, StringComparer.Ordinal)
            .Select(group => group.First())
            .FirstOrDefault(latest => latest.Verdict == "safe"
                && (string.Equals(latest.Url, target, StringComparison.Ordinal) || string.Equals(latest.FinalUrl, target, StringComparison.Ordinal)));
        if (match is null)
        {
            return Unrecognized();
        }

        return new OutboundLinkResponse(OutboundLinkStatus.Ok, target, ToUnicode(requested.IdnHost), ToUnicode(match.FinalHost));
    }

    private static OutboundLinkResponse Unrecognized() => new(OutboundLinkStatus.Unrecognized);

    private static string ToUnicode(string host)
    {
        try
        {
            return new IdnMapping().GetUnicode(host);
        }
        catch (ArgumentException)
        {
            return host;
        }
    }
}
