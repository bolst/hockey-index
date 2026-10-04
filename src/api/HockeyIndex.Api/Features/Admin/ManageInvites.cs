using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Features.Admin;

/// <summary>Signup invites for <c>Signup:Mode = invite_only</c>.</summary>
public sealed class ManageInvites(AppDbContext db, AuditRecorder audit, IClock clock)
{
    public const int DefaultExpiryDays = 14;
    public const int MaxExpiryDays = 90;
    public const int ListLimit = 200;

    public async Task<IReadOnlyList<InviteResponse>> ListAsync(CancellationToken cancellationToken) =>
    [
        .. (await db.SignupInvites.AsNoTracking().OrderByDescending(invite => invite.Id).Take(ListLimit).ToListAsync(cancellationToken))
            .Select(InviteResponse.From),
    ];

    public async Task<IResult> CreateAsync(InviteCreateRequest request, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var phone = PhoneNormalizer.Normalize(request.Phone);
        if (phone.Phone is null)
        {
            return AdminProblems.Invalid("invalid_phone", "Enter a valid US or Canadian mobile number.");
        }

        var days = request.ExpiresInDays ?? DefaultExpiryDays;
        if (days is < 1 or > MaxExpiryDays)
        {
            return AdminProblems.Invalid("invalid_expiry", $"Expiry must be 1 to {MaxExpiryDays} days.");
        }

        var invite = new SignupInvite
        {
            PhoneE164 = phone.Phone!.E164,
            CreatedBy = adminId,
            ExpiresAt = clock.GetCurrentInstant().ToDateTimeOffset().AddDays(days),
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.SignupInvites.Add(invite);
        audit.Record(adminId, "invite.create", AuditTargets.Invite, invite.Id.ToString(), reason, new { invite.ExpiresAt });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/v1/admin/invites/{invite.Id}", InviteResponse.From(invite));
    }

    public async Task<IResult> RevokeAsync(Guid id, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invite = await db.SignupInvites.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (invite is null)
        {
            return AdminProblems.InviteNotFound();
        }

        db.SignupInvites.Remove(invite);
        audit.Record(adminId, "invite.revoke", AuditTargets.Invite, id.ToString(), reason);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
