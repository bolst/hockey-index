using HockeyIndex.Api.Infrastructure.Text;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class UrlExtractorTests
{
    [Theory]
    [InlineData("Sign up at https://example.com/signup.", "https://example.com/signup")]
    [InlineData("Sign up (https://example.com/signup), thanks", "https://example.com/signup")]
    [InlineData("See https://en.wikipedia.org/wiki/Hockey_(disambiguation) today", "https://en.wikipedia.org/wiki/Hockey_(disambiguation)")]
    [InlineData("Really? https://example.com/a?b=1!", "https://example.com/a?b=1")]
    [InlineData("<https://example.com/x>", "https://example.com/x")]
    [InlineData("\"https://example.com/q\"", "https://example.com/q")]
    [InlineData("HTTPS://EXAMPLE.COM/Path", "https://example.com/Path")]
    [InlineData("Http://Example.com:80/x", "http://example.com/x")]
    [InlineData("https://example.com:443/x#section", "https://example.com/x")]
    [InlineData("https://example.com./x", "https://example.com/x")]
    public void Extracts_and_canonicalizes(string text, string expected)
    {
        var extraction = UrlExtractor.Extract(text);

        var url = Assert.Single(extraction.Urls);
        Assert.Equal(expected, url.Url.AbsoluteUri);
        Assert.Empty(extraction.Rejected);
    }

    [Fact]
    public void Converts_idn_hosts_to_punycode()
    {
        var url = Assert.Single(UrlExtractor.Extract("Info: https://bücher.example/kurs").Urls);

        Assert.Equal("https://xn--bcher-kva.example/kurs", url.Url.AbsoluteUri);
        Assert.Equal("https://bücher.example/kurs", url.Original);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("mailto:host@example.com")]
    [InlineData("ftp://example.com/file")]
    [InlineData("www.example.com without scheme")]
    [InlineData("xhttp://example.com")]
    [InlineData("")]
    public void Ignores_non_http_text(string text)
    {
        var extraction = UrlExtractor.Extract(text);

        Assert.Empty(extraction.Urls);
        Assert.Empty(extraction.Rejected);
    }

    [Theory]
    [InlineData("https://user:pass@example.com/", UrlRejection.UserInfo)]
    [InlineData("https://google.com@evil.example/", UrlRejection.UserInfo)]
    [InlineData("https://@evil.example/", UrlRejection.UserInfo)]
    [InlineData("https://example.com:8443/", UrlRejection.NonDefaultPort)]
    [InlineData("http://example.com:443/", UrlRejection.NonDefaultPort)]
    [InlineData("https:///nohost", UrlRejection.InvalidUrl)]
    public void Rejects_disallowed_urls(string text, UrlRejection expected)
    {
        var extraction = UrlExtractor.Extract(text);

        Assert.Empty(extraction.Urls);
        Assert.Equal(expected, Assert.Single(extraction.Rejected).Reason);
    }

    [Fact]
    public void Deduplicates_by_canonical_form()
    {
        var extraction = UrlExtractor.Extract("https://example.com/a HTTPS://EXAMPLE.COM/a#top https://example.com/b");

        Assert.Equal(["https://example.com/a", "https://example.com/b"], extraction.Urls.Select(u => u.Url.AbsoluteUri));
    }

    [Fact]
    public void Flags_more_than_ten_urls()
    {
        var ten = string.Join(' ', Enumerable.Range(1, 10).Select(i => $"https://example.com/{i}"));

        Assert.False(UrlExtractor.Extract(ten).TooManyUrls);
        Assert.True(UrlExtractor.Extract(ten + " https://example.com/11").TooManyUrls);
    }

    [Theory]
    [InlineData("javascript:alert(1)", UrlRejection.UnsupportedScheme)]
    [InlineData("file:///etc/passwd", UrlRejection.UnsupportedScheme)]
    [InlineData("https://example.com:8080/", UrlRejection.NonDefaultPort)]
    public void TryNormalize_rejects_redirect_targets(string candidate, UrlRejection expected)
    {
        Assert.False(UrlExtractor.TryNormalize(candidate, out _, out var rejection));
        Assert.Equal(expected, rejection);
    }

    [Fact]
    public void Decimal_and_octal_ip_hosts_normalize_to_dotted_form()
    {
        Assert.True(UrlExtractor.TryNormalize("http://2130706433/", out var decimalUrl, out _));
        Assert.True(UrlExtractor.TryNormalize("http://0177.0.0.1/", out var octalUrl, out _));

        Assert.Equal("127.0.0.1", decimalUrl.Host);
        Assert.Equal("127.0.0.1", octalUrl.Host);
    }

    [Theory]
    [InlineData("Example.COM.", "example.com")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    public void ToAsciiHost_lowercases_and_punycodes(string host, string expected) =>
        Assert.Equal(expected, UrlExtractor.ToAsciiHost(host));
}
