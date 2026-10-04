using HockeyIndex.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HockeyIndex.Api.Features.Admin;

/// <summary>Body of every admin write that needs nothing but the required reason.</summary>
public sealed record AdminReasonRequest(string? Reason);

public sealed record BlocklistAddRequest(string? Kind, string? Value, string? Reason);

public sealed record InviteCreateRequest(string? Phone, int? ExpiresInDays, string? Reason);

/// <param name="Action"><c>open</c> or <c>close</c>.</param>
/// <param name="Minutes">How long to open for (default 60, at most 7 days).</param>
public sealed record BreakerChangeRequest(string? Action, int? Minutes, string? Reason);

/// <summary>Null fields stay unchanged. Changing the time zone re-derives the instants of the venue's non-archived events.</summary>
public sealed record VenueEditRequest(
    string? Name, string? AddressLine, string? City, string? Region, string? Country, double? Latitude, double? Longitude, string? TimeZone, string? Reason);

/// <param name="IntoId">Public id of the venue that absorbs this one.</param>
public sealed record VenueMergeRequest(string? IntoId, string? Reason);

public sealed record ReportSummary(string Reason, string? Details, DateTimeOffset CreatedAt);

public sealed record QueueItem(
    string PublicId,
    string Title,
    string Status,
    string? HiddenReason,
    Guid HostId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int UnreviewedReports,
    IReadOnlyList<ReportSummary> Reports);

public sealed record AdminEventSummary(string PublicId, string Title, string Status, string? HiddenReason, DateTimeOffset StartsAt, DateTimeOffset EndsAt)
{
    public static AdminEventSummary From(HockeyEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return new(
            evt.PublicId, evt.Title, SnakeCaseText.Of(evt.Status), evt.HiddenReason is { } reason ? SnakeCaseText.Of(reason) : null,
            evt.StartsAt, evt.EndsAt);
    }
}

public sealed record AdminEventResponse(AdminEventSummary Event);

public sealed record AdminHostResponse(
    Guid Id,
    string? Phone,
    string? Email,
    string Status,
    bool IsAdmin,
    DateTimeOffset? BannedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<AdminEventSummary> Events);

public sealed record BanResponse(Guid HostId, string Status, int EventsHidden);

public sealed record BlocklistEntryResponse(Guid Id, string Kind, string Value, string Reason, Guid? CreatedBy, DateTimeOffset CreatedAt)
{
    public static BlocklistEntryResponse From(BlocklistEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(entry.Id, entry.Kind == BlocklistKind.Phone ? "phone" : "domain", entry.Value, entry.Reason, entry.CreatedBy, entry.CreatedAt);
    }
}

public sealed record InviteResponse(Guid Id, string Phone, Guid? CreatedBy, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt)
{
    public static InviteResponse From(SignupInvite invite)
    {
        ArgumentNullException.ThrowIfNull(invite);
        return new(invite.Id, invite.PhoneE164, invite.CreatedBy, invite.ExpiresAt, invite.UsedAt);
    }
}

public sealed record BreakerResponse(string Name, bool IsOpen, DateTimeOffset? OpenUntil, string? Reason, string? OpenedBy);

public sealed record AuditEntryResponse(
    long Id, Guid ActorId, string Action, string TargetType, string TargetId, string Reason, string? Metadata, DateTimeOffset CreatedAt)
{
    public static AuditEntryResponse From(AuditLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(entry.Id, entry.ActorId, entry.Action, entry.TargetType, entry.TargetId, entry.Reason, entry.Metadata, entry.CreatedAt);
    }
}

/// <param name="NextCursor">Pass as <c>cursor</c> for the next (older) page; null on the last page.</param>
public sealed record AuditPage(IReadOnlyList<AuditEntryResponse> Items, long? NextCursor);

internal static class AdminReason
{
    /// <returns>The trimmed reason, or null when it is missing, blank or too long.</returns>
    public static string? Validate(string? reason)
    {
        var trimmed = reason?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > AuditLogEntry.ReasonMaxLength ? null : trimmed;
    }
}

internal static class AdminProblems
{
    public static IResult ReasonRequired() => Of(
        StatusCodes.Status422UnprocessableEntity, "reason_required", $"Give a reason (1 to {AuditLogEntry.ReasonMaxLength} characters).");

    public static IResult Invalid(string code, string title) => Of(StatusCodes.Status422UnprocessableEntity, code, title);
    public static IResult HostNotFound() => Of(StatusCodes.Status404NotFound, "host_not_found", "Host not found.");
    public static IResult HostBanned() => Of(StatusCodes.Status409Conflict, "host_banned", "The host is banned. Unban the host first.");
    public static IResult HostNotBanned() => Of(StatusCodes.Status409Conflict, "host_not_banned", "The host is not banned.");
    public static IResult CannotBanSelf() => Of(StatusCodes.Status409Conflict, "cannot_ban_self", "You cannot ban your own account.");
    public static IResult BlocklistNotFound() => Of(StatusCodes.Status404NotFound, "blocklist_entry_not_found", "Blocklist entry not found.");
    public static IResult BlocklistDuplicate() => Of(StatusCodes.Status409Conflict, "blocklist_duplicate", "This value is already blocklisted.");
    public static IResult InviteNotFound() => Of(StatusCodes.Status404NotFound, "invite_not_found", "Invite not found.");
    public static IResult BreakerNotFound() => Of(StatusCodes.Status404NotFound, "breaker_not_found", "Unknown breaker.");
    public static IResult VenueMergeInvalid(string title) => Of(StatusCodes.Status409Conflict, "venue_merge_invalid", title);

    private static ProblemHttpResult Of(int status, string code, string title) =>
        TypedResults.Problem(title: title, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
