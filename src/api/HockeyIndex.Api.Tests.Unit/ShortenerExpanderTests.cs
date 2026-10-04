using HockeyIndex.Api.Features.Safety;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class ShortenerExpanderTests : IDisposable
{
    private readonly WireMockServer server = WireMockServer.Start();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Non_shortener_urls_resolve_without_any_request()
    {
        var expansion = await Expander().ExpandAsync(new Uri("https://example.com/signup"), Ct);

        Assert.Equal(ExpansionOutcome.Resolved, expansion.Outcome);
        Assert.Equal("https://example.com/signup", expansion.FinalUrl.AbsoluteUri);
        Assert.Equal(0, expansion.RedirectHops);
        Assert.Empty(server.LogEntries);
    }

    [Fact]
    public async Task Follows_a_redirect_to_the_first_non_shortener_target()
    {
        Redirect("/a", "https://tinyurl.com/b");
        Redirect("/b", "https://example.com/final");

        var expansion = await Expander().ExpandAsync(new Uri("https://bit.ly/a"), Ct);

        Assert.Equal(ExpansionOutcome.Resolved, expansion.Outcome);
        Assert.Equal("https://example.com/final", expansion.FinalUrl.AbsoluteUri);
        Assert.Equal(2, expansion.RedirectHops);
        Assert.All(server.LogEntries, entry => Assert.Equal("HEAD", entry.RequestMessage!.Method));
    }

    [Fact]
    public async Task Resolves_relative_locations_against_the_current_url()
    {
        Redirect("/a", "/b");
        Redirect("/b", "https://example.com/final");

        var expansion = await Expander().ExpandAsync(new Uri("https://bit.ly/a"), Ct);

        Assert.Equal(["https://bit.ly/a", "https://bit.ly/b", "https://example.com/final"], expansion.Chain.Select(u => u.AbsoluteUri));
    }

    [Fact]
    public async Task Falls_back_to_get_when_head_is_not_supported()
    {
        server.Given(Request.Create().WithPath("/a").UsingHead()).RespondWith(Response.Create().WithStatusCode(405));
        server.Given(Request.Create().WithPath("/a").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", "https://example.com/final"));

        var expansion = await Expander().ExpandAsync(new Uri("https://bit.ly/a"), Ct);

        Assert.Equal("https://example.com/final", expansion.FinalUrl.AbsoluteUri);
        Assert.Equal(["HEAD", "GET"], server.LogEntries.Select(entry => entry.RequestMessage!.Method));
    }

    [Fact]
    public async Task Shortener_without_redirect_is_its_own_destination()
    {
        server.Given(Request.Create().WithPath("/a").UsingHead()).RespondWith(Response.Create().WithStatusCode(200));

        var expansion = await Expander().ExpandAsync(new Uri("https://bit.ly/a"), Ct);

        Assert.Equal(ExpansionOutcome.Resolved, expansion.Outcome);
        Assert.Equal("https://bit.ly/a", expansion.FinalUrl.AbsoluteUri);
    }

    [Fact]
    public async Task Six_hop_chain_stops_after_five_requests()
    {
        for (var i = 1; i <= 6; i++)
        {
            Redirect($"/h{i}", i < 6 ? $"https://bit.ly/h{i + 1}" : "https://example.com/final");
        }

        var expansion = await Expander().ExpandAsync(new Uri("https://bit.ly/h1"), Ct);

        Assert.Equal(ExpansionOutcome.RedirectLimitExceeded, expansion.Outcome);
        Assert.Equal(5, expansion.RedirectHops);
        Assert.Equal(5, server.LogEntries.Count);
    }

    [Fact]
    public async Task Five_hop_chain_resolves()
    {
        for (var i = 1; i <= 5; i++)
        {
            Redirect($"/h{i}", i < 5 ? $"https://bit.ly/h{i + 1}" : "https://example.com/final");
        }

        var expansion = await Expander().ExpandAsync(new Uri("https://bit.ly/h1"), Ct);

        Assert.Equal(ExpansionOutcome.Resolved, expansion.Outcome);
        Assert.Equal(5, expansion.RedirectHops);
        Assert.Equal("https://example.com/final", expansion.FinalUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user@example.com/")]
    [InlineData("https://example.com:8080/")]
    [InlineData("http://bit.ly:8080/next")]
    [InlineData("file:///etc/passwd")]
    public async Task Disallowed_redirect_targets_stop_expansion(string location)
    {
        Redirect("/a", location);

        var expansion = await Expander().ExpandAsync(new Uri("https://bit.ly/a"), Ct);

        Assert.Equal(ExpansionOutcome.DisallowedRedirect, expansion.Outcome);
        Assert.Single(server.LogEntries);
    }

    [Fact]
    public async Task Slow_hop_times_out_as_failed()
    {
        server.Given(Request.Create().WithPath("/slow").UsingHead())
            .RespondWith(Response.Create().WithStatusCode(301).WithHeader("Location", "https://example.com/").WithDelay(TimeSpan.FromSeconds(2)));

        var expansion = await Expander(perHop: TimeSpan.FromMilliseconds(200)).ExpandAsync(new Uri("https://bit.ly/slow"), Ct);

        Assert.Equal(ExpansionOutcome.Failed, expansion.Outcome);
    }

    [Fact]
    public async Task Chain_exceeding_the_total_budget_fails()
    {
        for (var i = 1; i <= 4; i++)
        {
            server.Given(Request.Create().WithPath($"/t{i}").UsingHead())
                .RespondWith(Response.Create().WithStatusCode(301).WithHeader("Location", $"https://bit.ly/t{i + 1}").WithDelay(TimeSpan.FromMilliseconds(150)));
        }

        var expansion = await Expander(perHop: TimeSpan.FromSeconds(1), total: TimeSpan.FromMilliseconds(400))
            .ExpandAsync(new Uri("https://bit.ly/t1"), Ct);

        Assert.Equal(ExpansionOutcome.Failed, expansion.Outcome);
        Assert.True(server.LogEntries.Count < 4);
    }

    [Fact]
    public async Task Network_error_is_failed()
    {
        server.Stop();

        var expansion = await Expander().ExpandAsync(new Uri("https://bit.ly/a"), Ct);

        Assert.Equal(ExpansionOutcome.Failed, expansion.Outcome);
    }

    [Fact]
    public void Shortener_allowlist_is_configurable()
    {
        var expander = Expander(hosts: ["sho.rt"]);

        Assert.True(expander.IsShortener(new Uri("https://SHO.RT/x")));
        Assert.False(expander.IsShortener(new Uri("https://bit.ly/x")));
    }

    public void Dispose() => server.Dispose();

    private void Redirect(string path, string location) =>
        server.Given(Request.Create().WithPath(path).UsingHead())
            .RespondWith(Response.Create().WithStatusCode(301).WithHeader("Location", location));

    private ShortenerExpander Expander(TimeSpan? perHop = null, TimeSpan? total = null, string[]? hosts = null)
    {
        var options = new LinkScanningOptions
        {
            PerHopTimeout = perHop ?? TimeSpan.FromSeconds(3),
            TotalExpansionTimeout = total ?? TimeSpan.FromSeconds(8),
            ShortenerHosts = hosts,
        };
#pragma warning disable CA2000 // HttpClient owns and disposes the handler chain.
        var http = new HttpClient(new RouteToServerHandler(new Uri(server.Url!)) { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false } });
#pragma warning restore CA2000
        return new ShortenerExpander(http, Options.Create(options));
    }

    /// <summary>Sends every request to WireMock, keeping the path, so tests can use real shortener host names.</summary>
    private sealed class RouteToServerHandler(Uri server) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri(server, request.RequestUri!.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
