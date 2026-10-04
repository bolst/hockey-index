using System.Threading.Channels;

namespace HockeyIndex.Api.Infrastructure.Caching;

/// <summary>Purges edge-cached public responses for events. Purge only accelerates the 60 s TTL; failures are logged and counted.</summary>
public interface ICachePurger
{
    /// <returns>False when any part of the purge failed after retries.</returns>
    Task<bool> PurgeAsync(IReadOnlyCollection<string> eventPublicIds, CancellationToken cancellationToken);
}

/// <summary>
/// In-memory queue drained by one background reader. Callers enqueue only after their transaction commits
/// (<c>EventTransitions.CommitAsync</c>), so a rolled-back change never purges.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Name fixed by the US-008 spec; it is a work queue.")]
public sealed partial class CachePurgeQueue(IServiceProvider services, ILogger<CachePurgeQueue> logger) : BackgroundService
{
    public const int MaxBatchSize = 30;

    private readonly Channel<string> channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private long enqueuedCount;

    /// <summary>Total public ids accepted since startup.</summary>
    public long EnqueuedCount => Interlocked.Read(ref enqueuedCount);

    public void Enqueue(IEnumerable<string> eventPublicIds)
    {
        ArgumentNullException.ThrowIfNull(eventPublicIds);
        foreach (var publicId in eventPublicIds)
        {
            if (channel.Writer.TryWrite(publicId))
            {
                Interlocked.Increment(ref enqueuedCount);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                var batch = new HashSet<string>(StringComparer.Ordinal);
                while (batch.Count < MaxBatchSize && reader.TryRead(out var publicId))
                {
                    batch.Add(publicId);
                }

                try
                {
                    await services.GetRequiredService<ICachePurger>().PurgeAsync(batch, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogPurgeFailed(logger, exception, batch.Count);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Cache purge of {Count} events failed")]
    private static partial void LogPurgeFailed(ILogger logger, Exception exception, int count);
}

public static class CachePurgeSetup
{
    /// <summary>Registers the queue. <see cref="ICachePurger"/> comes from the integrations switch.</summary>
    public static IServiceCollection AddCachePurging(this IServiceCollection services)
    {
        services.AddSingleton<CachePurgeQueue>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<CachePurgeQueue>());
        return services;
    }
}
