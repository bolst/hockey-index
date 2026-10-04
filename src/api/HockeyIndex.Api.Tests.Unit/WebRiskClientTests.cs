using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Integrations.WebRisk;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class WebRiskClientTests : IDisposable
{
    private const string ApiKey = "test-key";
    private readonly WireMockServer server = WireMockServer.Start();
    private readonly HttpClient http = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Threat_match_is_malicious_with_expiry()
    {
        Respond(200, """{"threat":{"threatTypes":["MALWARE","SOCIAL_ENGINEERING"],"expireTime":"2026-10-04T12:00:00.123456789Z"}}""");

        var result = await Client().LookupAsync(new Uri("https://bad.example/x"), Ct);

        Assert.Equal(UrlReputationStatus.Malicious, result.Status);
        Assert.Equal(["MALWARE", "SOCIAL_ENGINEERING"], result.ThreatTypes);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero), result.ExpiresAt?.AddTicks(-result.ExpiresAt.Value.Ticks % TimeSpan.TicksPerSecond));
    }

    [Fact]
    public async Task Empty_response_is_safe()
    {
        Respond(200, "{}");

        var result = await Client().LookupAsync(new Uri("https://good.example/"), Ct);

        Assert.Equal(UrlReputationStatus.Safe, result.Status);
    }

    [Fact]
    public async Task Sends_key_in_header_and_requests_all_three_threat_types()
    {
        Respond(200, "{}");

        await Client().LookupAsync(new Uri("https://good.example/a?b=1"), Ct);

        var request = Assert.Single(server.LogEntries).RequestMessage!;
        Assert.Equal("/v1/uris:search", request.Path);
        Assert.Equal(ApiKey, request.Headers![WebRiskClient.ApiKeyHeader].Single());
        Assert.Equal(["MALWARE", "SOCIAL_ENGINEERING", "UNWANTED_SOFTWARE"], request.Query!["threatTypes"]);
        Assert.Equal("https://good.example/a?b=1", request.Query["uri"].Single());
        Assert.DoesNotContain(ApiKey, request.Url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(403)]
    public async Task Provider_errors_fail_as_provider_faults(int status)
    {
        Respond(status, "{}");

        var result = await Client().LookupAsync(new Uri("https://good.example/"), Ct);

        Assert.Equal(UrlReputationStatus.Failed, result.Status);
        Assert.True(result.IsProviderFault);
    }

    [Fact]
    public async Task Bad_request_fails_without_blaming_the_provider()
    {
        Respond(400, """{"error":{"code":400}}""");

        var result = await Client().LookupAsync(new Uri("https://good.example/"), Ct);

        Assert.Equal(UrlReputationStatus.Failed, result.Status);
        Assert.False(result.IsProviderFault);
    }

    [Fact]
    public async Task Timeout_and_malformed_body_are_provider_faults()
    {
        server.Given(Request.Create().WithPath("/v1/uris:search").WithParam("uri", "https://slow.example/"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}").WithDelay(TimeSpan.FromSeconds(2)));
        server.Given(Request.Create().WithPath("/v1/uris:search").WithParam("uri", "https://garbled.example/"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("not json"));

        var client = Client(TimeSpan.FromMilliseconds(200));
        var slow = await client.LookupAsync(new Uri("https://slow.example/"), Ct);
        var garbled = await client.LookupAsync(new Uri("https://garbled.example/"), Ct);

        Assert.True(slow is { Status: UrlReputationStatus.Failed, IsProviderFault: true });
        Assert.True(garbled is { Status: UrlReputationStatus.Failed, IsProviderFault: true });
    }

    [Theory]
    [InlineData("https://testsafebrowsing.appspot.com/s/phishing.html", UrlReputationStatus.Malicious)]
    [InlineData("https://scam.malicious.test/pay", UrlReputationStatus.Malicious)]
    [InlineData("https://down.unavailable.test/", UrlReputationStatus.Failed)]
    [InlineData("https://example.com/", UrlReputationStatus.Safe)]
    public async Task Fake_provider_follows_documented_markers(string url, UrlReputationStatus expected)
    {
        var result = await new FakeUrlReputationProvider().LookupAsync(new Uri(url), Ct);

        Assert.Equal(expected, result.Status);
    }

    public void Dispose()
    {
        http.Dispose();
        server.Dispose();
    }

    private void Respond(int status, string body) =>
        server.Given(Request.Create().WithPath("/v1/uris:search").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

    private WebRiskClient Client(TimeSpan? timeout = null) =>
        new(
            http,
            Options.Create(new WebRiskOptions { ApiKey = ApiKey, BaseUrl = new Uri(server.Url!), Timeout = timeout ?? TimeSpan.FromSeconds(5) }),
            NullLogger<WebRiskClient>.Instance);
}
