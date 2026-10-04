namespace HockeyIndex.Api.Domain;

public enum ReportReason
{
    Scam,
    Spam,
    Offensive,
    Inaccurate,
    Other,
}

/// <summary>An anonymous report. One per event per reporter (HMAC of the IPv4 address or IPv6 /64).</summary>
public sealed class Report
{
    public const int DetailsMaxLength = 500;

    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid EventId { get; init; }
    public required byte[] ReporterHash { get; init; }
    public required ReportReason Reason { get; init; }
    public string? Details { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Set when an admin restores the event, so a new auto-hide needs three new reporters.</summary>
    public DateTimeOffset? ReviewedAt { get; set; }
}
