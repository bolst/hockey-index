namespace HockeyIndex.Api.Domain;

public static class OtpChannels
{
    public const string Sms = "sms";
    public const string Email = "email";
}

public static class OtpKinds
{
    public const string Send = "send";
    public const string Verify = "verify";
}

public static class OtpOutcomes
{
    public const string Sent = "sent";
    public const string Verified = "verified";
    public const string Failed = "failed";
    public const string Limited = "limited";
    public const string RejectedRegion = "rejected_region";
    public const string RejectedBlocklist = "rejected_blocklist";
    public const string RejectedBanned = "rejected_banned";
    public const string RejectedLineType = "rejected_line_type";
    public const string TurnstileFailed = "turnstile_failed";
    public const string BreakerOpen = "breaker_open";
    public const string BudgetExhausted = "budget_exhausted";
    public const string InviteRequired = "invite_required";

    public static readonly string[] All =
    [
        Sent, Verified, Failed, Limited, RejectedRegion, RejectedBlocklist, RejectedBanned, RejectedLineType,
        TurnstileFailed, BreakerOpen, BudgetExhausted, InviteRequired,
    ];

    /// <summary>Send outcomes recorded after a send passed the limiter; these count toward the hourly limits.</summary>
    public static readonly string[] CountedSends = [Sent, Failed, BreakerOpen, BudgetExhausted, RejectedLineType];
}

/// <summary>OTP audit and limit window. Targets and IP keys are HMAC hashes; rows are purged after 30 days.</summary>
public sealed class OtpLogEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Channel { get; init; }
    public required string Kind { get; init; }
    public byte[]? TargetHash { get; init; }
    public required byte[] IpKeyHash { get; init; }
    public string? NpaNxx { get; init; }
    public required string Outcome { get; set; }

    /// <summary>Twilio Lookup line type; doubles as the 30-day lookup cache.</summary>
    public string? LineType { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}
