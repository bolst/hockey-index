using HockeyIndex.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HockeyIndex.Api.Infrastructure.Persistence.Locks;

/// <summary>
/// Host-scoped serialization shared by publish, restore and ban: a class-<see cref="AdvisoryLockClasses.HostPublish"/>
/// advisory lock plus <c>SELECT … FROM hosts WHERE id = @h FOR UPDATE</c>. MUST run inside a transaction.
/// </summary>
public static class HostLock
{
    /// <returns>The host's status, or null when the host does not exist.</returns>
    public static async Task<HostStatus?> AcquireAsync(AppDbContext db, Guid hostId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException($"{nameof(HostLock)} MUST be taken inside a transaction.");
        }

        var key = hostId.ToString();
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockClasses.HostPublish}, hashtext({key}))", cancellationToken);
        var statuses = await db.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM hosts WHERE id = {hostId} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return statuses switch
        {
            [] => null,
            ["banned"] => HostStatus.Banned,
            _ => HostStatus.Active,
        };
    }
}
