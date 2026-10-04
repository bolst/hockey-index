using HockeyIndex.Api.Infrastructure.RateLimiting;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class OtpBreakerRulesTests
{
    private static readonly OtpOptions Defaults = new();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(49, 0)]
    [InlineData(50, 13)]
    [InlineData(300, 300)]
    public void Stays_closed_under_thresholds(int sends, int verified)
    {
        Assert.Null(OtpBreakerRules.EvaluateLastHour(sends, verified, Defaults));
    }

    [Fact]
    public void Opens_above_300_sends_per_hour()
    {
        Assert.StartsWith("volume", OtpBreakerRules.EvaluateLastHour(301, 301, Defaults), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(50, 12)]
    [InlineData(100, 0)]
    public void Opens_when_conversion_is_below_25_percent_over_50_sends(int sends, int verified)
    {
        Assert.StartsWith("conversion", OtpBreakerRules.EvaluateLastHour(sends, verified, Defaults), StringComparison.Ordinal);
    }
}
