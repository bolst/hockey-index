using System.Text.Json;
using HockeyIndex.Api.Infrastructure.Caching;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Integrations.Cloudflare;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class CloudflareCachePurgerTests : IDisposable
{
    private const string ZoneId = "zone123";
    private const string ApiToken = "cf-test-token";
    private const string PurgePath = $"/client/v4/zones/{ZoneId}/purge_cache";
    private readonly WireMockServer cloudflare = WireMockServer.Start();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tag_mode_is_the_default_and_purges_by_cache_tag()
    {
        Respond(200);
        await using var services = BuildServices();
        using var collector = Collector(services);

        var succeeded = await services.GetRequiredService<ICachePurger>().PurgeAsync(["abcdefghij", "klmnopqrst"], Ct);

        Assert.True(succeeded);
        var request = Assert.Single(cloudflare.LogEntries).RequestMessage!;
        Assert.Equal("POST", request.Method);
        Assert.Equal(PurgePath, request.Path);
        Assert.Equal($"Bearer {ApiToken}", request.Headers!["Authorization"].Single());
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(["event-abcdefghij", "event-klmnopqrst"], body.RootElement.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.False(body.RootElement.TryGetProperty("files", out _));
        AssertSingleMeasurement(collector, CachePurgeResults.Success);
    }

    [Fact]
    public async Task Url_mode_purges_public_event_urls()
    {
        Respond(200);
        await using var services = BuildServices(new()
        {
            ["Cloudflare:PurgeMode"] = CachePurgeModes.Url,
            ["Cloudflare:PublicApiBaseUrl"] = "https://api.example.test/",
        });

        var succeeded = await services.GetRequiredService<ICachePurger>().PurgeAsync(["abcdefghij"], Ct);

        Assert.True(succeeded);
        var request = Assert.Single(cloudflare.LogEntries).RequestMessage!;
        Assert.Equal(PurgePath, request.Path);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(["https://api.example.test/v1/events/abcdefghij"], body.RootElement.GetProperty("files").EnumerateArray().Select(file => file.GetString()));
        Assert.False(body.RootElement.TryGetProperty("tags", out _));
    }

    [Fact]
    public async Task Failures_retry_three_times_then_count_an_error()
    {
        Respond(500);
        await using var services = BuildServices();
        using var collector = Collector(services);

        var succeeded = await services.GetRequiredService<ICachePurger>().PurgeAsync(["abcdefghij"], Ct);

        Assert.False(succeeded);
        Assert.Equal(4, cloudflare.LogEntries.Count);
        AssertSingleMeasurement(collector, CachePurgeResults.Error);
    }

    [Fact]
    public async Task A_retry_that_succeeds_counts_one_success()
    {
        cloudflare.Given(Request.Create().WithPath(PurgePath).UsingPost())
            .InScenario("flaky").WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(503));
        cloudflare.Given(Request.Create().WithPath(PurgePath).UsingPost())
            .InScenario("flaky").WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"success":true}"""));
        await using var services = BuildServices();
        using var collector = Collector(services);

        var succeeded = await services.GetRequiredService<ICachePurger>().PurgeAsync(["abcdefghij"], Ct);

        Assert.True(succeeded);
        Assert.Equal(2, cloudflare.LogEntries.Count);
        AssertSingleMeasurement(collector, CachePurgeResults.Success);
    }

    [Fact]
    public async Task Batches_larger_than_thirty_split_into_separate_calls()
    {
        Respond(200);
        await using var services = BuildServices();
        var ids = Enumerable.Range(0, CloudflareCachePurger.MaxItemsPerCall + 1).Select(n => $"id{n:D8}").ToArray();

        await services.GetRequiredService<ICachePurger>().PurgeAsync(ids, Ct);

        Assert.Equal(2, cloudflare.LogEntries.Count);
    }

    [Fact]
    public void Missing_credentials_fail_options_validation()
    {
        using var services = BuildServices(new() { ["Cloudflare:ApiToken"] = "" });

        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => services.GetRequiredService<ICachePurger>());
    }

    public void Dispose() => cloudflare.Dispose();

    private void Respond(int statusCode) =>
        cloudflare.Given(Request.Create().WithPath(PurgePath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithBody("""{"success":true}"""));

    private ServiceProvider BuildServices(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Cloudflare:ApiToken"] = ApiToken,
            ["Cloudflare:ZoneId"] = ZoneId,
            ["Cloudflare:BaseUrl"] = cloudflare.Urls[0] + "/",
            ["Cloudflare:RetryDelay"] = "00:00:00",
        };
        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddMetrics()
            .AddSingleton<HockeyIndexMetrics>()
            .AddCloudflareCachePurger()
            .BuildServiceProvider();
    }

    private static MetricCollector<long> Collector(IServiceProvider services) =>
        new(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), HockeyIndexMetrics.MeterName, HockeyIndexMetrics.CfPurgeTotalName);

    private static void AssertSingleMeasurement(MetricCollector<long> collector, string result)
    {
        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal(result, measurement.Tags[HockeyIndexMetrics.ResultTag]);
    }
}
