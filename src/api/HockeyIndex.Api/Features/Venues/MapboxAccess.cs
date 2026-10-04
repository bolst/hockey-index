using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HockeyIndex.Api.Features.Venues;

/// <summary>The <c>mapbox</c> breaker and the <c>mapbox_temp</c>/<c>mapbox_perm</c> daily budgets that gate every paid call.</summary>
public sealed partial class MapboxAccess(
    BreakerService breakers, ProviderBudget budget, IClock clock, IOptions<VenueOptions> options, ILogger<MapboxAccess> logger)
{
    public Task<bool> IsBreakerOpenAsync(CancellationToken cancellationToken) => breakers.IsOpenAsync(BreakerNames.Mapbox, cancellationToken);

    public Task<bool> TryChargeAsync(string provider, Guid hostId, CancellationToken cancellationToken) =>
        budget.TryConsumeAsync(provider, hostId, cancellationToken);

    public async Task OpenBreakerAsync(CancellationToken cancellationToken)
    {
        var openFor = options.Value.BreakerOpenFor;
        await breakers.OpenAsync(
            BreakerNames.Mapbox, clock.GetCurrentInstant().ToDateTimeOffset() + openFor, "provider_error", BreakerOpener.Auto, cancellationToken);
        LogBreakerOpened(logger, openFor);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opened mapbox breaker for {Duration} after a provider fault")]
    private static partial void LogBreakerOpened(ILogger logger, TimeSpan duration);
}
