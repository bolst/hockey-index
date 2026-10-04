using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HockeyIndex.Api.Infrastructure.Text;

public enum UrlRejection
{
    InvalidUrl,
    UnsupportedScheme,
    UserInfo,
    NonDefaultPort,
}

/// <param name="Original">The URL as written in the text, trailing punctuation removed.</param>
/// <param name="Url">Canonical form: lowercase scheme and punycode host, no default port, no fragment.</param>
public sealed record ExtractedUrl(string Original, Uri Url);

public sealed record RejectedUrl(string Original, UrlRejection Reason);

public sealed record UrlExtraction(IReadOnlyList<ExtractedUrl> Urls, IReadOnlyList<RejectedUrl> Rejected)
{
    public bool TooManyUrls => Urls.Count > UrlExtractor.MaxUrls;
}

/// <summary>
/// Finds http(s) URLs in free text. Other schemes (javascript:, data:, mailto:) are never extracted.
/// Linkifiers that render this text MUST link exactly the URLs this extractor returns.
/// </summary>
public static partial class UrlExtractor
{
    public const int MaxUrls = 10;

    private const string TrailingPunctuation = ".,;:!?*";

    public static UrlExtraction Extract(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new UrlExtraction([], []);
        }

        var urls = new List<ExtractedUrl>();
        var rejected = new List<RejectedUrl>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in CandidatePattern().Matches(text))
        {
            var candidate = TrimTrailing(match.Value);
            if (!TryNormalize(candidate, out var url, out var rejection))
            {
                rejected.Add(new RejectedUrl(candidate, rejection));
            }
            else if (seen.Add(url.AbsoluteUri))
            {
                urls.Add(new ExtractedUrl(candidate, url));
            }
        }

        return new UrlExtraction(urls, rejected);
    }

    /// <summary>Validates an absolute http(s) URL and returns its canonical form. Also used to re-validate redirect targets.</summary>
    public static bool TryNormalize(string candidate, [NotNullWhen(true)] out Uri? url, out UrlRejection rejection)
    {
        url = null;
        rejection = UrlRejection.InvalidUrl;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!IsHttpScheme(parsed.Scheme))
        {
            rejection = UrlRejection.UnsupportedScheme;
            return false;
        }

        if (string.IsNullOrEmpty(parsed.Host))
        {
            return false;
        }

        if (parsed.UserInfo.Length > 0 || AuthorityOf(candidate).Contains('@', StringComparison.Ordinal))
        {
            rejection = UrlRejection.UserInfo;
            return false;
        }

        if (!parsed.IsDefaultPort)
        {
            rejection = UrlRejection.NonDefaultPort;
            return false;
        }

        string host;
        try
        {
            host = parsed.IdnHost.TrimEnd('.');
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (host.Length == 0)
        {
            return false;
        }

        var authority = parsed.HostNameType == UriHostNameType.IPv6 ? $"[{host}]" : host;
        return Uri.TryCreate($"{parsed.Scheme}://{authority}{parsed.PathAndQuery}", UriKind.Absolute, out url);
    }

    /// <summary>Lowercase punycode form of a host name, for comparing hosts with blocklist entries.</summary>
    public static string ToAsciiHost(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var trimmed = host.Trim().TrimEnd('.');
        try
        {
            return new IdnMapping().GetAscii(trimmed).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return trimmed.ToLowerInvariant();
        }
    }

    private static bool IsHttpScheme(string scheme) =>
        scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static string AuthorityOf(string candidate)
    {
        var start = candidate.IndexOf("://", StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var authority = candidate[(start + 3)..];
        var end = authority.IndexOfAny(['/', '?', '#', '\\']);
        return end < 0 ? authority : authority[..end];
    }

    private static string TrimTrailing(string candidate)
    {
        while (candidate.Length > 0)
        {
            var last = candidate[^1];
            if (TrailingPunctuation.Contains(last, StringComparison.Ordinal)
                || (last == ')' && IsUnbalanced(candidate, '(', ')'))
                || (last == ']' && IsUnbalanced(candidate, '[', ']'))
                || (last == '}' && IsUnbalanced(candidate, '{', '}')))
            {
                candidate = candidate[..^1];
            }
            else
            {
                break;
            }
        }

        return candidate;
    }

    private static bool IsUnbalanced(string candidate, char open, char close) =>
        candidate.Count(c => c == close) > candidate.Count(c => c == open);

    [GeneratedRegex("""(?<![\w+.\-])https?://[^\s<>"'`]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CandidatePattern();
}
