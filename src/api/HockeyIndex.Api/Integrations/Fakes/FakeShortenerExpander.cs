using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Infrastructure.Text;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Integrations.Fakes;

/// <summary>
/// Expands allowlisted shortener URLs without network access. By path prefix: <c>/unreachable</c> → failed,
/// <c>/loop</c> → redirect limit, <c>/malicious</c> → <c>https://malicious.test/…</c>; anything else →
/// <c>https://example.com/expanded/…</c>. Non-shortener URLs resolve to themselves.
/// </summary>
public sealed class FakeShortenerExpander(IOptions<LinkScanningOptions> options) : IShortenerExpander
{
    private readonly HashSet<string> shortenerHosts = new(
        (options.Value.ShortenerHosts ?? LinkScanningOptions.DefaultShortenerHosts).Select(UrlExtractor.ToAsciiHost),
        StringComparer.Ordinal);

    public bool IsShortener(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return shortenerHosts.Contains(UrlExtractor.ToAsciiHost(url.Host));
    }

    public Task<ShortenerExpansion> ExpandAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!IsShortener(url))
        {
            return Task.FromResult(new ShortenerExpansion(ExpansionOutcome.Resolved, url, [url]));
        }

        var path = url.AbsolutePath;
        var expansion = path switch
        {
            _ when path.StartsWith("/unreachable", StringComparison.Ordinal) => new ShortenerExpansion(ExpansionOutcome.Failed, url, [url]),
            _ when path.StartsWith("/loop", StringComparison.Ordinal) => new ShortenerExpansion(ExpansionOutcome.RedirectLimitExceeded, url, [url]),
            _ when path.StartsWith("/malicious", StringComparison.Ordinal) => Resolved(url, new Uri($"https://malicious.test{path}")),
            _ => Resolved(url, new Uri($"https://example.com/expanded{path}")),
        };
        return Task.FromResult(expansion);
    }

    private static ShortenerExpansion Resolved(Uri shortUrl, Uri finalUrl) => new(ExpansionOutcome.Resolved, finalUrl, [shortUrl, finalUrl]);
}
