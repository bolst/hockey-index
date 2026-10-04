using System.Collections.Concurrent;
using HockeyIndex.Api.Infrastructure.Caching;

namespace HockeyIndex.Api.Integrations.Fakes;

/// <summary>Records purges in memory; makes no network call.</summary>
public sealed class FakeCachePurger : ICachePurger
{
    private readonly ConcurrentQueue<string> purged = new();

    public IReadOnlyCollection<string> Purged => purged;

    public Task<bool> PurgeAsync(IReadOnlyCollection<string> eventPublicIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventPublicIds);
        foreach (var publicId in eventPublicIds)
        {
            purged.Enqueue(publicId);
        }

        return Task.FromResult(true);
    }
}

public static class FakeCachePurgerSetup
{
    public static IServiceCollection AddFakeCachePurger(this IServiceCollection services) =>
        services.AddSingleton<ICachePurger, FakeCachePurger>();
}
