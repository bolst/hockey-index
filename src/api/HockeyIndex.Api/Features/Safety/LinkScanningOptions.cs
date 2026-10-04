namespace HockeyIndex.Api.Features.Safety;

public sealed class LinkScanningOptions
{
    public const string SectionName = "LinkScanning";

    public static readonly IReadOnlyList<string> DefaultShortenerHosts =
    [
        "bit.ly", "bitly.com", "buff.ly", "cutt.ly", "forms.gle", "goo.gl", "is.gd", "lnkd.in", "ow.ly",
        "rb.gy", "rebrand.ly", "shorturl.at", "t.co", "t.ly", "tiny.cc", "tinyurl.com",
    ];

    /// <summary>Overrides <see cref="DefaultShortenerHosts"/> when set.</summary>
    public IReadOnlyList<string>? ShortenerHosts { get; set; }

    public int MaxRedirectHops { get; set; } = 5;

    public TimeSpan PerHopTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public TimeSpan TotalExpansionTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>How long the <c>webrisk</c> breaker stays open after a provider fault.</summary>
    public TimeSpan BreakerOpenFor { get; set; } = TimeSpan.FromMinutes(5);
}
