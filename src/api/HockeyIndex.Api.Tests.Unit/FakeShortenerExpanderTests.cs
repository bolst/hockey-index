using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Integrations.Fakes;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class FakeShortenerExpanderTests
{
    private readonly FakeShortenerExpander expander = new(Options.Create(new LinkScanningOptions()));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Non_shortener_urls_resolve_to_themselves()
    {
        var url = new Uri("https://example.org/join");

        var expansion = await expander.ExpandAsync(url, Ct);

        Assert.Equal(ExpansionOutcome.Resolved, expansion.Outcome);
        Assert.Equal(url, expansion.FinalUrl);
    }

    [Theory]
    [InlineData("https://bit.ly/unreachable", ExpansionOutcome.Failed)]
    [InlineData("https://bit.ly/loop", ExpansionOutcome.RedirectLimitExceeded)]
    public async Task Path_prefixes_simulate_failures(string url, ExpansionOutcome expected)
    {
        var expansion = await expander.ExpandAsync(new Uri(url), Ct);

        Assert.Equal(expected, expansion.Outcome);
    }

    [Theory]
    [InlineData("https://bit.ly/malicious/x", "https://malicious.test/malicious/x")]
    [InlineData("https://bit.ly/abc", "https://example.com/expanded/abc")]
    public async Task Shortener_urls_expand_without_network(string url, string expectedFinal)
    {
        var expansion = await expander.ExpandAsync(new Uri(url), Ct);

        Assert.Equal(ExpansionOutcome.Resolved, expansion.Outcome);
        Assert.Equal(expectedFinal, expansion.FinalUrl.AbsoluteUri);
        Assert.Equal(1, expansion.RedirectHops);
    }

    [Fact]
    public void Recognises_default_shortener_hosts() => Assert.True(expander.IsShortener(new Uri("https://bit.ly/a")));
}
