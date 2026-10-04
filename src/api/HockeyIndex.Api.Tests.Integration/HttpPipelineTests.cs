using System.Net;
using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class HttpPipelineTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Missing_client_ip_header_on_public_listener_is_rejected()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Non_public_endpoint_defaults_to_private_no_store()
    {
        using var client = Factory.CreatePublicClient();

        using var response = await client.GetAsync(new Uri("/v1/host/venues", UriKind.Relative), Ct);

        Assert.Equal("private, no-store", response.Headers.NonValidated["Cache-Control"].ToString());
    }

    [Fact]
    public async Task Public_endpoint_allows_any_origin_and_sets_no_cookie()
    {
        using var client = Factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Add("Origin", "https://example.org");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Set_cookie_on_public_endpoint_is_stripped_and_counted()
    {
        var factory = CreateFactory(configureServices: s => s.AddSingleton<IStartupFilter, SetCookieInjector>());
        using var collector = new MetricCollector<long>(
            factory.Services.GetRequiredService<IMeterFactory>(),
            HockeyIndexMetrics.MeterName,
            HockeyIndexMetrics.PublicSetCookieStrippedName);
        using var client = factory.CreatePublicClient();

        using var publicResponse = await client.GetAsync(new Uri("/healthz", UriKind.Relative), Ct);
        using var privateResponse = await client.GetAsync(new Uri("/v1/me", UriKind.Relative), Ct);

        Assert.False(publicResponse.Headers.Contains("Set-Cookie"));
        Assert.True(privateResponse.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, Assert.Single(collector.GetMeasurementSnapshot()).Value);
    }

    [Fact]
    public async Task Credentialed_path_echoes_spa_origin_with_credentials()
    {
        using var client = Factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
        request.Headers.Add("Origin", "https://hockeyindex.com");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal("https://hockeyindex.com", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        Assert.Contains("Origin", response.Headers.Vary);
    }

    [Fact]
    public async Task Credentialed_path_ignores_other_origins()
    {
        using var client = Factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, Ct);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Contains("Origin", response.Headers.Vary);
    }

    [Fact]
    public async Task Public_preflight_succeeds_without_credentials()
    {
        using var client = Factory.CreatePublicClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/events/abc/reports");
        request.Headers.Add("Origin", "https://example.org");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Authentication_runs_only_on_non_public_paths()
    {
        var probe = new AuthenticationProbe();
        var factory = CreateFactory(configureServices: s => s.AddSingleton<IStartupFilter>(probe));
        using var client = factory.CreatePublicClient();

        using var publicResponse = await client.GetAsync(new Uri("/healthz", UriKind.Relative), Ct);
        using var privateResponse = await client.GetAsync(new Uri("/v1/me", UriKind.Relative), Ct);

        Assert.False(probe.AuthenticationRan["/healthz"]);
        Assert.True(probe.AuthenticationRan["/v1/me"]);
    }

    [Fact]
    public async Task Public_read_limit_applies_per_client_ip()
    {
        var factory = CreateFactory(new Dictionary<string, string?> { ["RateLimiting:PublicReadPerMinute"] = "2" });
        using var first = factory.CreatePublicClient("198.51.100.1");
        using var second = factory.CreatePublicClient("198.51.100.2");

        var firstStatuses = await GetStatusesAsync(first, 3);
        var secondStatuses = await GetStatusesAsync(second, 1);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], firstStatuses);
        Assert.Equal([HttpStatusCode.OK], secondStatuses);
    }

    [Fact]
    public async Task Edge_requests_share_the_global_edge_bucket()
    {
        var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Edge:Key"] = "edge-secret",
            ["RateLimiting:PublicReadPerMinute"] = "5",
            ["RateLimiting:PublicReadEdgePerMinute"] = "1",
        });
        using var edgeA = factory.CreatePublicClient("198.51.100.1");
        using var edgeB = factory.CreatePublicClient("198.51.100.2");
        edgeA.DefaultRequestHeaders.Add(EdgeKeyAuth.HeaderName, "edge-secret");
        edgeB.DefaultRequestHeaders.Add(EdgeKeyAuth.HeaderName, "edge-secret");
        using var direct = factory.CreatePublicClient("198.51.100.1");
        direct.DefaultRequestHeaders.Add(EdgeKeyAuth.HeaderName, "wrong-key");

        Assert.Equal([HttpStatusCode.OK], await GetStatusesAsync(edgeA, 1));
        Assert.Equal([HttpStatusCode.TooManyRequests], await GetStatusesAsync(edgeB, 1));
        Assert.Equal([HttpStatusCode.OK], await GetStatusesAsync(direct, 1));
    }

    private static async Task<HttpStatusCode[]> GetStatusesAsync(HttpClient client, int count)
    {
        var statuses = new HttpStatusCode[count];
        for (var i = 0; i < count; i++)
        {
            using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative), Ct);
            statuses[i] = response.StatusCode;
        }

        return statuses;
    }
}
