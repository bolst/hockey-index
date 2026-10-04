using Microsoft.Extensions.DependencyInjection;

namespace HockeyIndex.Api.Tests.Integration.Infrastructure;

/// <summary>Resets the shared database before each test and owns a default <see cref="ApiFactory"/>.</summary>
[Collection(PostgisTestGroup.Name)]
public abstract class IntegrationTest(PostgisFixture postgis) : IAsyncLifetime
{
    private readonly List<ApiFactory> factories = [];

    protected PostgisFixture Postgis { get; } = postgis;

    protected ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Postgis.ResetAsync();
        Factory = CreateFactory();
    }

    protected ApiFactory CreateFactory(
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new ApiFactory(Postgis, settings, configureServices);
        factories.Add(factory);
        return factory;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in factories)
        {
            await factory.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
