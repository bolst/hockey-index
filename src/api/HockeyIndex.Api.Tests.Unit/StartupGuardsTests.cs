using HockeyIndex.Api.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class StartupGuardsTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("1")]
    public void Production_with_fake_providers_refuses_to_start(string fakeProviders)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.EnsureSafeConfiguration(Environment(Environments.Production), Configuration(fakeProviders)));

        Assert.Contains("HI_FAKE_PROVIDERS", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Production_without_fake_providers_starts(string? fakeProviders) =>
        StartupGuards.EnsureSafeConfiguration(Environment(Environments.Production), Configuration(fakeProviders));

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void Non_production_allows_fake_providers(string environmentName) =>
        StartupGuards.EnsureSafeConfiguration(Environment(environmentName), Configuration("true"));

    private static IConfiguration Configuration(string? fakeProviders) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["HI_FAKE_PROVIDERS"] = fakeProviders })
            .Build();

    private static TestHostEnvironment Environment(string name) => new() { EnvironmentName = name };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public required string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "HockeyIndex.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
