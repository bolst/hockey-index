using Microsoft.AspNetCore.Identity;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Infrastructure.Auth;

/// <summary>A host is rooted in its phone: the number is required, cannot be cleared, and is also the user name.</summary>
public sealed class HostUserValidator : IUserValidator<Host>
{
    public Task<IdentityResult> ValidateAsync(UserManager<Host> manager, Host user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(user.PhoneNumber))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "PhoneRequired", Description = "A phone number is required." }));
        }

        return Task.FromResult(user.UserName == user.PhoneNumber
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError { Code = "PhoneMismatch", Description = "The user name must be the phone number." }));
    }
}
