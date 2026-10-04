using System.Security.Claims;

namespace HockeyIndex.Api.Infrastructure.Auth;

public static class HostClaims
{
    /// <summary>The signed-in host's id from the Identity cookie, or <c>null</c> for anonymous principals.</summary>
    public static Guid? GetHostId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
