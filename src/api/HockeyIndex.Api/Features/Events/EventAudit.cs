namespace HockeyIndex.Api.Features.Events;

/// <param name="Reason">The admin's required reason, stored in <c>audit_log.reason</c>.</param>
/// <param name="Metadata">Extra <c>audit_log.metadata</c> keys (for example <c>cap_override</c>).</param>
public sealed record AdminAudit(string Reason, IReadOnlyDictionary<string, object?>? Metadata = null);

/// <param name="Id">Host id for host and admin actions; null for jobs.</param>
/// <param name="Audit">Set for an admin action that <see cref="EventTransitions"/> MUST write to <c>audit_log</c>.</param>
public sealed record TransitionActor(Guid? Id, AdminAudit? Audit = null)
{
    public static TransitionActor System { get; } = new((Guid?)null);

    public bool IsAdmin => Audit is not null;

    public static TransitionActor Host(Guid hostId) => new(hostId);

    public static TransitionActor Admin(Guid adminId, string reason, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(adminId, new AdminAudit(reason, metadata));

    /// <summary>A transition caused by a parent admin action (ban) whose single audit row covers it.</summary>
    public static TransitionActor AdminCascade(Guid adminId) => new(adminId);
}
