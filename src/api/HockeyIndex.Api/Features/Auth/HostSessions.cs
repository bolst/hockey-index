using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Admin;
using HockeyIndex.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NodaTime;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Features.Auth;

public sealed class HostSessions(
    UserManager<Host> userManager, SignInManager<Host> signInManager, AuditRecorder audit, IOptions<AdminOptions> adminOptions, IClock clock)
{
    /// <summary>Records the login and issues the persistent <c>__Host-hi_auth</c> cookie.</summary>
    public async Task<IResult> SignInAsync(Host host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.LastLoginAt = clock.GetCurrentInstant().ToDateTimeOffset();
        if (!host.IsAdmin && IsBootstrapAdmin(host.PhoneNumber))
        {
            host.IsAdmin = true;
            audit.Record(host.Id, "admin.bootstrap", AuditTargets.Host, host.Id.ToString(), AdminOptions.BootstrapReason);
        }

        EnsureSucceeded(await userManager.UpdateAsync(host));
        await signInManager.SignInAsync(host, isPersistent: true);
        return TypedResults.Ok(MeResponse.From(host));
    }

    /// <summary>Re-issues the cookie after a security-stamp change so this session survives while others end.</summary>
    public Task RefreshAsync(Host host) => signInManager.RefreshSignInAsync(host);

    private bool IsBootstrapAdmin(string? phone) =>
        phone is not null
        && adminOptions.Value.BootstrapPhones.Any(candidate => PhoneNormalizer.Normalize(candidate).Phone?.E164 == phone);

    public static void EnsureSucceeded(IdentityResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Host update failed: {string.Join(", ", result.Errors.Select(error => error.Code))}");
        }
    }
}
