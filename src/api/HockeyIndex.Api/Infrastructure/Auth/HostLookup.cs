using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Infrastructure.Auth;

/// <summary>Host queries that Identity's <c>UserManager</c> gets wrong for this model (it can match unconfirmed emails).</summary>
public sealed class HostLookup(AppDbContext db)
{
    public Task<Host?> FindByConfirmedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(host => host.NormalizedEmail == normalizedEmail && host.EmailConfirmed, cancellationToken);

    public Task<Host?> FindByPhoneAsync(string e164, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(host => host.PhoneNumber == e164, cancellationToken);
}
