namespace HockeyIndex.Api.Domain;

public static class BreakerNames
{
    public const string OtpSms = "otp_sms";
    public const string Mapbox = "mapbox";
    public const string WebRisk = "webrisk";
}

public enum BreakerOpener
{
    Auto,
    TwilioWebhook,
    Admin,
}

public sealed class Breaker
{
    public required string Name { get; init; }
    public DateTimeOffset? OpenUntil { get; set; }
    public string? Reason { get; set; }
    public BreakerOpener? OpenedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
