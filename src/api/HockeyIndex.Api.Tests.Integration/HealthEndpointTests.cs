using System.Net;
using Microsoft.AspNetCore.Http;
using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Tests.Integration.Infrastructure;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class HealthEndpointTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    [Fact]
    public async Task Healthz_returns_ok()
    {
        using var client = Factory.CreatePublicClient();

        using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"status":"ok"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Readyz_on_ops_listener_reports_ready_after_migrations()
    {
        var context = await Factory.SendOnPortAsync(ListenerPorts.Ops, "/readyz");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Readyz_on_public_listener_is_not_found()
    {
        var context = await Factory.SendOnPortAsync(
            ListenerPorts.Public,
            "/readyz",
            request => request.Headers[ClientIpResolver.HeaderName] = ApiFactory.ClientIp);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task Readyz_responses_are_not_cacheable()
    {
        var context = await Factory.SendOnPortAsync(ListenerPorts.Ops, "/readyz");

        Assert.Equal("private, no-store", context.Response.Headers.CacheControl.ToString());
    }
}
