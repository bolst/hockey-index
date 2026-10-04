using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using HockeyIndex.Api.Integrations.WebRisk;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class LinkScanServiceTests(PostgisFixture postgis) : IntegrationTest(postgis), IDisposable
{
    private const string SafeUrl = "https://example.com/register";
    private const string BadUrl = "https://phish.example/login";
    private readonly WireMockServer webRisk = WireMockServer.Start();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Malicious_verdict_blocks_and_a_cache_hit_skips_web_risk_and_budget()
    {
        RespondThreat(BadUrl, """{"threat":{"threatTypes":["SOCIAL_ENGINEERING"],"expireTime":"2026-10-03T18:00:00Z"}}""");
        var factory = WebRiskFactory();
        var host = Guid.CreateVersion7();

        var first = await ScanAsync(factory, $"Register here: {BadUrl}", host);
        var second = await ScanAsync(factory, $"Register here: {BadUrl}", host);

        Assert.Equal(LinkScanStatus.Blocked, first.Status);
        Assert.Equal(LinkScanStatus.Blocked, second.Status);
        var link = Assert.Single(second.Links);
        Assert.Equal(LinkVerdictSource.WebRisk, link.Source);
        Assert.Equal(["SOCIAL_ENGINEERING"], link.ThreatTypes);
        Assert.Equal([BadUrl], second.BlockedUrls);
        Assert.Single(webRisk.LogEntries);
        Assert.Equal(1, await CallsAsync(host));

        var cached = await CachedVerdictAsync(BadUrl);
        Assert.Equal(UrlVerdictValues.Malicious, cached.Verdict);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 18, 0, 0, TimeSpan.Zero), cached.ExpiresAt);
    }

    [Fact]
    public async Task Malicious_verdict_is_honoured_until_expire_time()
    {
        RespondThreat(BadUrl, """{"threat":{"threatTypes":["MALWARE"],"expireTime":"2026-10-03T12:30:00Z"}}""");
        var factory = WebRiskFactory();

        await ScanAsync(factory, BadUrl);
        factory.Clock.Advance(Duration.FromMinutes(29));
        await ScanAsync(factory, BadUrl);
        Assert.Single(webRisk.LogEntries);

        factory.Clock.Advance(Duration.FromMinutes(2));
        await ScanAsync(factory, BadUrl);
        Assert.Equal(2, webRisk.LogEntries.Count);
    }

    [Fact]
    public async Task Safe_verdict_is_cached_for_an_hour()
    {
        RespondSafe();
        var factory = WebRiskFactory();

        var first = await ScanAsync(factory, $"Sign up: {SafeUrl}");
        factory.Clock.Advance(Duration.FromMinutes(59));
        await ScanAsync(factory, SafeUrl);
        Assert.Single(webRisk.LogEntries);

        factory.Clock.Advance(Duration.FromMinutes(2));
        await ScanAsync(factory, SafeUrl);

        Assert.Equal(LinkScanStatus.Clean, first.Status);
        Assert.Equal(LinkVerdict.Safe, Assert.Single(first.Links).Verdict);
        Assert.Equal(2, webRisk.LogEntries.Count);
        Assert.Equal(UrlVerdictValues.Safe, (await CachedVerdictAsync(SafeUrl)).Verdict);
    }

    [Fact]
    public async Task Provider_failure_is_unavailable_and_opens_the_breaker()
    {
        webRisk.Given(Request.Create().WithPath("/v1/uris:search")).RespondWith(Response.Create().WithStatusCode(503));
        var factory = WebRiskFactory();

        var first = await ScanAsync(factory, SafeUrl);
        var second = await ScanAsync(factory, "https://other.example/");

        Assert.Equal(LinkScanStatus.Unavailable, first.Status);
        Assert.Equal(LinkScanReason.ProviderError, Assert.Single(first.Links).Reason);
        Assert.Equal(LinkScanStatus.Unavailable, second.Status);
        Assert.Equal(LinkScanReason.BreakerOpen, Assert.Single(second.Links).Reason);
        Assert.Single(webRisk.LogEntries);
        Assert.True(await IsBreakerOpenAsync(factory));
        await using var db = Postgis.CreateDbContext();
        Assert.Empty(await db.UrlVerdicts.ToListAsync(Ct));
    }

    [Fact]
    public async Task Host_at_its_cap_is_unavailable_while_another_host_still_scans()
    {
        RespondSafe();
        var factory = WebRiskFactory(perHostCap: 2);
        var hostA = Guid.CreateVersion7();
        var hostB = Guid.CreateVersion7();

        await ScanAsync(factory, "https://a1.example/", hostA);
        await ScanAsync(factory, "https://a2.example/", hostA);
        var hostAOverCap = await ScanAsync(factory, "https://a3.example/", hostA);
        var hostBResult = await ScanAsync(factory, "https://b1.example/", hostB);

        Assert.Equal(LinkScanStatus.Unavailable, hostAOverCap.Status);
        Assert.Equal(LinkScanReason.BudgetExhausted, Assert.Single(hostAOverCap.Links).Reason);
        Assert.Equal(LinkScanStatus.Clean, hostBResult.Status);
        Assert.Equal(3, webRisk.LogEntries.Count);
        Assert.Equal(2, await CallsAsync(hostA));
        Assert.Equal(1, await CallsAsync(hostB));
    }

    [Fact]
    public async Task Blocklisted_domain_is_blocked_before_web_risk_and_budget()
    {
        RespondSafe();
        var factory = WebRiskFactory();
        await using (var db = Postgis.CreateDbContext())
        {
            db.Blocklist.Add(new BlocklistEntry
            {
                Kind = BlocklistKind.Domain,
                Value = "scam.example",
                Reason = "test",
                CreatedAt = factory.Clock.GetCurrentInstant().ToDateTimeOffset(),
            });
            await db.SaveChangesAsync(Ct);
        }

        var host = Guid.CreateVersion7();
        var result = await ScanAsync(factory, "Pay at https://pay.SCAM.example/now and https://example.com/", host);

        Assert.Equal(LinkScanStatus.Blocked, result.Status);
        Assert.Equal(["https://pay.SCAM.example/now"], result.BlockedUrls);
        Assert.Equal("https://pay.scam.example/now", result.Links[0].Url.AbsoluteUri);
        Assert.Equal(LinkVerdictSource.Blocklist, result.Links[0].Source);
        Assert.Single(webRisk.LogEntries);
        Assert.Equal(1, await CallsAsync(host));
    }

    [Fact]
    public async Task Too_many_or_disallowed_urls_block_without_any_lookup()
    {
        RespondSafe();
        var factory = WebRiskFactory();
        var eleven = string.Join(' ', Enumerable.Range(1, 11).Select(i => $"https://example.com/{i}"));

        var tooMany = await ScanAsync(factory, eleven);
        var userInfo = await ScanAsync(factory, "https://google.com@evil.example/");
        var none = await ScanAsync(factory, "No links, javascript:alert(1)");

        Assert.True(tooMany is { Status: LinkScanStatus.Blocked, TooManyUrls: true });
        Assert.Equal(LinkScanStatus.Blocked, userInfo.Status);
        Assert.NotEmpty(userInfo.RejectedUrls);
        Assert.Equal(LinkScanStatus.Clean, none.Status);
        Assert.Empty(webRisk.LogEntries);
    }

    [Fact]
    public async Task Fake_provider_is_used_when_fake_providers_are_enabled()
    {
        var result = await ScanAsync(Factory, "https://testsafebrowsing.appspot.com/s/phishing.html https://down.unavailable.test/");

        Assert.Equal(LinkScanStatus.Blocked, result.Status);
        Assert.Equal([LinkVerdict.Malicious, LinkVerdict.Unknown], result.Links.Select(link => link.Verdict));
    }

    [Fact]
    public async Task Scanning_inside_a_transaction_is_refused()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<LinkScanService>().ScanAsync(SafeUrl, null, Ct));
    }

    public void Dispose() => webRisk.Dispose();

    private ApiFactory WebRiskFactory(int perHostCap = 200) =>
        CreateFactory(
            new Dictionary<string, string?>
            {
                ["WebRisk:ApiKey"] = "test-key",
                ["WebRisk:BaseUrl"] = webRisk.Url + "/",
                ["Providers:Caps:webrisk:PerHost"] = perHostCap.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Providers:Caps:webrisk:Global"] = "1000",
            },
            services => services.AddWebRiskClient());

    private void RespondSafe() =>
        webRisk.Given(Request.Create().WithPath("/v1/uris:search"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("{}"));

    private void RespondThreat(string url, string body) =>
        webRisk.Given(Request.Create().WithPath("/v1/uris:search").WithParam("uri", url))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body));

    private static async Task<LinkScanResult> ScanAsync(ApiFactory factory, string text, Guid? hostId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LinkScanService>().ScanAsync(text, hostId, Ct);
    }

    private static async Task<bool> IsBreakerOpenAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BreakerService>().IsOpenAsync(BreakerNames.WebRisk, Ct);
    }

    private async Task<UrlVerdict> CachedVerdictAsync(string url)
    {
        var hash = UrlVerdictCache.HashOf(new Uri(url));
        await using var db = Postgis.CreateDbContext();
        return await db.UrlVerdicts.SingleAsync(verdict => verdict.UrlHash == hash, Ct);
    }

    private async Task<int> CallsAsync(Guid hostId)
    {
        await using var db = Postgis.CreateDbContext();
        return await db.ProviderUsage
            .Where(usage => usage.Provider == ProviderNames.WebRisk && usage.HostId == hostId)
            .Select(usage => usage.Calls)
            .SingleOrDefaultAsync(Ct);
    }
}
