using System.Security.Claims;
using HockeyIndex.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Infrastructure.Auth;

/// <summary>Security stamp check (every <see cref="AuthSetup.SecurityStampInterval"/>) that also ends banned hosts' sessions.</summary>
public sealed class HostSecurityStampValidator(
    IOptions<SecurityStampValidatorOptions> options,
    SignInManager<Host> signInManager,
    ILoggerFactory logger) : SecurityStampValidator<Host>(options, signInManager, logger)
{
    protected override async Task<Host?> VerifySecurityStamp(ClaimsPrincipal? principal)
    {
        var host = await base.VerifySecurityStamp(principal);
        return host is { Status: HostStatus.Banned } ? null : host;
    }
}
