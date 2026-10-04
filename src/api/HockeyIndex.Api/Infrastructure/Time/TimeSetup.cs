using NodaTime;

namespace HockeyIndex.Api.Infrastructure.Time;

public static class TimeSetup
{
    /// <summary>
    /// Registers NodaTime's <see cref="IClock"/> (tests replace it with <c>NodaTime.Testing.FakeClock</c>) and a
    /// <see cref="TimeProvider"/> that reads the same clock, so cookie expiry and security-stamp checks follow it.
    /// </summary>
    public static IServiceCollection AddClock(this IServiceCollection services)
    {
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IDateTimeZoneProvider>(DateTimeZoneProviders.Tzdb);
        services.AddSingleton<TimeProvider>(serviceProvider => new ClockTimeProvider(serviceProvider.GetRequiredService<IClock>()));
        return services;
    }
}

internal sealed class ClockTimeProvider(IClock clock) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => clock.GetCurrentInstant().ToDateTimeOffset();
}
