namespace HockeyIndex.Api.Domain;

/// <summary>One scanned URL of an event's join instructions.</summary>
public sealed class LinkScan
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid EventId { get; init; }
    public required string Url { get; init; }
    public required string FinalUrl { get; init; }
    public required string FinalHost { get; init; }
    public required int RedirectHops { get; init; }

    /// <summary><c>safe</c>, <c>malicious</c> or <c>unknown</c>.</summary>
    public required string Verdict { get; init; }

    /// <summary><c>webrisk</c>, <c>blocklist</c> or <c>none</c>.</summary>
    public required string Provider { get; init; }

    public string[] ThreatTypes { get; init; } = [];
    public required DateTimeOffset CheckedAt { get; init; }
}
