namespace HockeyIndex.Api.Domain;

public static class AuditTargets
{
    public const string Event = "event";
    public const string Host = "host";
    public const string Blocklist = "blocklist";
    public const string Invite = "invite";
    public const string Breaker = "breaker";
    public const string Venue = "venue";
}

/// <summary>
/// Append-only record of an admin action (<c>audit_log</c>). <c>hi_app</c> has no UPDATE, DELETE or TRUNCATE on the
/// table and a trigger rejects UPDATE and DELETE. No foreign keys, so purges never touch it.
/// </summary>
public sealed class AuditLogEntry
{
    public const int ReasonMaxLength = 500;

    public long Id { get; init; }
    public required Guid ActorId { get; init; }
    public required string Action { get; init; }
    public required string TargetType { get; init; }
    public required string TargetId { get; init; }
    public required string Reason { get; init; }

    /// <summary>JSON object stored as <c>jsonb</c>.</summary>
    public string? Metadata { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
