using System.Text.Json;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using NodaTime;

namespace HockeyIndex.Api.Features.Admin;

/// <summary>
/// Adds <c>audit_log</c> rows to the caller's <see cref="AppDbContext"/>, so each row commits (or rolls back) with the
/// change it records. Callers save; this class never calls <c>SaveChanges</c>.
/// </summary>
public sealed class AuditRecorder(AppDbContext db, IClock clock)
{
    private static readonly JsonSerializerOptions MetadataJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <param name="metadata">Serialized as a JSON object with snake_case keys; null stores SQL NULL.</param>
    public void Record(Guid actorId, string action, string targetType, string targetId, string reason, object? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Admin actions MUST carry a reason.", nameof(reason));
        }

        db.AuditLog.Add(new AuditLogEntry
        {
            ActorId = actorId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Reason = reason.Trim(),
            Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata, MetadataJson),
            CreatedAt = clock.GetCurrentInstant().ToDateTimeOffset(),
        });
    }
}
