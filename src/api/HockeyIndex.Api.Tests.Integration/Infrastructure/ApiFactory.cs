using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Integrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Testing;

namespace HockeyIndex.Api.Tests.Integration.Infrastructure;

public sealed class ApiFactory(
    PostgisFixture postgis,
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    public const string ClientIp = "203.0.113.10";

    public FakeClock Clock { get; } = new(Instant.FromUtc(2026, 10, 3, 12, 0));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", postgis.AppConnectionString);
        builder.UseSetting(FakeProviders.ConfigKey, "true");
        builder.UseSetting("Jobs:Enabled", "false");
        builder.UseSetting("Hmac:Key", "integration-test-hmac-key-0123456789abcdef");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft.AspNetCore.DataProtection", "Error");
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IClock>(Clock);
            configureServices?.Invoke(services);
        });
    }

    /// <summary>A client that looks like traffic arriving through Cloudflare on the public listener.</summary>
    public HttpClient CreatePublicClient(string clientIp = ClientIp)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(ClientIpResolver.HeaderName, clientIp);
        return client;
    }

    /// <summary>
    /// TestServer reports LocalPort 0; this sets it so listener-specific behaviour (8080 public, 8081 ops) is testable.
    /// </summary>
    public Task<HttpContext> SendOnPortAsync(int port, string path, Action<HttpRequest>? configureRequest = null, string method = "GET") =>
        Server.SendAsync(context =>
        {
            context.Connection.LocalPort = port;
            context.Request.Method = method;
            context.Request.Path = path;
            configureRequest?.Invoke(context.Request);
        });
}
