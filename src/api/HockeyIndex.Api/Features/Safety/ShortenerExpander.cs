using System.Net;
using HockeyIndex.Api.Infrastructure.Text;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Features.Safety;

public enum ExpansionOutcome
{
    /// <summary>The final URL is known: either the input was not a shortener or the chain left the shortener allowlist.</summary>
    Resolved,
    RedirectLimitExceeded,

    /// <summary>A redirect pointed at a URL the extractor would reject (scheme, userinfo, port).</summary>
    DisallowedRedirect,

    /// <summary>Timeout, network error, or refused connection; the destination is unknown.</summary>
    Failed,
}

public sealed record ShortenerExpansion(ExpansionOutcome Outcome, Uri FinalUrl, IReadOnlyList<Uri> Chain)
{
    public int RedirectHops => Chain.Count - 1;
}

public interface IShortenerExpander
{
    bool IsShortener(Uri url);

    Task<ShortenerExpansion> ExpandAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>
/// Follows redirects from allowlisted URL shorteners without reading bodies. Requests go only to allowlisted hosts;
/// the first redirect target outside the allowlist is the final URL. Every target is re-validated before use.
/// The <see cref="HttpClient"/> MUST use <see cref="Infrastructure.Http.SsrfSafeHandler"/> with auto-redirect off.
/// </summary>
public sealed class ShortenerExpander(HttpClient http, IOptions<LinkScanningOptions> options) : IShortenerExpander
{
    private readonly LinkScanningOptions settings = options.Value;
    private readonly HashSet<string> shortenerHosts = new(
        (options.Value.ShortenerHosts ?? LinkScanningOptions.DefaultShortenerHosts).Select(UrlExtractor.ToAsciiHost),
        StringComparer.Ordinal);

    public bool IsShortener(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return shortenerHosts.Contains(UrlExtractor.ToAsciiHost(url.Host));
    }

    public async Task<ShortenerExpansion> ExpandAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);

        var chain = new List<Uri> { url };
        if (!IsShortener(url))
        {
            return new ShortenerExpansion(ExpansionOutcome.Resolved, url, chain);
        }

        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(settings.TotalExpansionTimeout);

        var current = url;
        try
        {
            while (true)
            {
                if (chain.Count - 1 >= settings.MaxRedirectHops)
                {
                    return new ShortenerExpansion(ExpansionOutcome.RedirectLimitExceeded, current, chain);
                }

                var location = await FetchRedirectTargetAsync(current, total.Token);
                if (location is null)
                {
                    return new ShortenerExpansion(ExpansionOutcome.Resolved, current, chain);
                }

                if (!UrlExtractor.TryNormalize(new Uri(current, location).AbsoluteUri, out var next, out _))
                {
                    return new ShortenerExpansion(ExpansionOutcome.DisallowedRedirect, current, chain);
                }

                chain.Add(next);
                if (!IsShortener(next))
                {
                    return new ShortenerExpansion(ExpansionOutcome.Resolved, next, chain);
                }

                current = next;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ShortenerExpansion(ExpansionOutcome.Failed, current, chain);
        }
        catch (HttpRequestException)
        {
            return new ShortenerExpansion(ExpansionOutcome.Failed, current, chain);
        }
        catch (UriFormatException)
        {
            return new ShortenerExpansion(ExpansionOutcome.DisallowedRedirect, current, chain);
        }
    }

    private async Task<Uri?> FetchRedirectTargetAsync(Uri url, CancellationToken cancellationToken)
    {
        using var hop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        hop.CancelAfter(settings.PerHopTimeout);

        using (var head = await SendAsync(HttpMethod.Head, url, hop.Token))
        {
            if (IsRedirect(head.StatusCode) || (int)head.StatusCode < 400)
            {
                return RedirectTarget(head);
            }
        }

        using var get = await SendAsync(HttpMethod.Get, url, hop.Token);
        return RedirectTarget(get);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static Uri? RedirectTarget(HttpResponseMessage response) =>
        IsRedirect(response.StatusCode) ? response.Headers.Location : null;

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
