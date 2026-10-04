namespace HockeyIndex.Api.Infrastructure.RateLimiting;

/// <summary>OTP send and verify limits plus <c>otp_sms</c> breaker thresholds (section <c>Otp</c>).</summary>
public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    public int SmsPerPhonePerHour { get; set; } = 3;
    public int SmsPerIpPerHour { get; set; } = 10;
    public int SmsPerNpaNxxPerHour { get; set; } = 20;
    public int EmailPerAddressPerHour { get; set; } = 3;
    public int EmailPerIpPerHour { get; set; } = 10;
    public int ChecksPerTargetPer10Minutes { get; set; } = 5;

    public int BreakerMaxSendsPerHour { get; set; } = 300;
    public int BreakerMinSendsForConversion { get; set; } = 50;
    public double BreakerMinConversion { get; set; } = 0.25;
    public int BreakerOpenMinutes { get; set; } = 30;

    public int LineTypeCacheDays { get; set; } = 30;
    public int RecycledNumberAfterDays { get; set; } = 180;
}

public static class OtpBreakerRules
{
    /// <returns>The reason to open <c>otp_sms</c>, or <c>null</c> to leave it closed.</returns>
    public static string? EvaluateLastHour(int sends, int verified, OtpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (sends > options.BreakerMaxSendsPerHour)
        {
            return $"volume: {sends} sends in the last hour";
        }

        if (sends >= options.BreakerMinSendsForConversion && verified < sends * options.BreakerMinConversion)
        {
            return $"conversion: {verified}/{sends} verified in the last hour";
        }

        return null;
    }
}
