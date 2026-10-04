using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Persistence.Locks;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Features.Admin;

/// <summary>Host detail, ban and unban (AC-TS-5, R8, R16).</summary>
public sealed class ManageHosts(AppDbContext db, EventTransitions transitions, AuditRecorder audit, IClock clock)
{
    public async Task<IResult> GetAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var host = await db.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == hostId, cancellationToken);
        if (host is null)
        {
            return AdminProblems.HostNotFound();
        }

        var events = await db.Events.AsNoTracking()
            .Where(evt => evt.HostId == hostId)
            .OrderByDescending(evt => evt.StartsAt)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new AdminHostResponse(
            host.Id,
            host.PhoneNumber,
            host.Email,
            SnakeCaseText.Of(host.Status),
            host.IsAdmin,
            host.BannedAt,
            host.CreatedAt,
            host.LastLoginAt,
            [.. events.Select(AdminEventSummary.From)]));
    }

    /// <summary>
    /// Under <see cref="HostLock"/> (shared with publish and restore): mark banned, rotate the security stamp (live sessions end
    /// within a minute), clear parked publish requests, lock the host's events <c>ORDER BY id</c> and hide every published,
    /// cancelled and visible archived event with reason <c>host_banned</c>. One audit row; one purge batch after commit.
    /// </summary>
    public async Task<IResult> BanAsync(Guid hostId, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        if (hostId == adminId)
        {
            return AdminProblems.CannotBanSelf();
        }

        return await DeadlockRetry.RunAsync(ct => BanOnceAsync(hostId, reason, adminId, ct), cancellationToken);
    }

    public async Task<IResult> UnbanAsync(Guid hostId, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var status = await HostLock.AcquireAsync(db, hostId, cancellationToken);
        if (status is null)
        {
            return AdminProblems.HostNotFound();
        }

        if (status != HostStatus.Banned)
        {
            return AdminProblems.HostNotBanned();
        }

        var host = await db.Users.SingleAsync(candidate => candidate.Id == hostId, cancellationToken);
        host.Status = HostStatus.Active;
        host.BannedAt = null;
        host.ConcurrencyStamp = Guid.NewGuid().ToString();
        audit.Record(adminId, "host.unban", AuditTargets.Host, hostId.ToString(), reason);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(new BanResponse(hostId, SnakeCaseText.Of(HostStatus.Active), EventsHidden: 0));
    }

    private async Task<IResult> BanOnceAsync(Guid hostId, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var status = await HostLock.AcquireAsync(db, hostId, cancellationToken);
        if (status is null)
        {
            return AdminProblems.HostNotFound();
        }

        if (status == HostStatus.Banned)
        {
            return AdminProblems.HostBanned();
        }

        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        var host = await db.Users.SingleAsync(candidate => candidate.Id == hostId, cancellationToken);
        host.Status = HostStatus.Banned;
        host.BannedAt = now;
        host.SecurityStamp = Guid.NewGuid().ToString("N");
        host.ConcurrencyStamp = Guid.NewGuid().ToString();

        var events = await db.Events
            .FromSql($"SELECT e.*, e.xmin FROM events e WHERE e.host_id = {hostId} ORDER BY e.id FOR UPDATE")
            .ToListAsync(cancellationToken);
        var hidden = 0;
        foreach (var evt in events)
        {
            if (evt.PublishRequestedAt is not null)
            {
                evt.PublishRequestedAt = null;
                evt.UpdatedAt = now;
            }

            if (evt.Status is EventStatus.Published or EventStatus.Cancelled || (evt.Status == EventStatus.Archived && evt.HiddenReason is null))
            {
                transitions.Hide(evt, HiddenReason.HostBanned, TransitionActor.AdminCascade(adminId));
                hidden++;
            }
        }

        audit.Record(adminId, "host.ban", AuditTargets.Host, hostId.ToString(), reason, new { EventsHidden = hidden });
        await transitions.CommitAsync(transaction, cancellationToken);
        return TypedResults.Ok(new BanResponse(hostId, SnakeCaseText.Of(HostStatus.Banned), hidden));
    }
}
