using System.Security.Claims;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using HockeyIndex.Api.Integrations.Email;
using HockeyIndex.Api.Integrations.Turnstile;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using Npgsql;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Features.Auth;

/// <summary>
/// Optional secondary login by confirmed email. Only confirmed addresses can sign in, and start never reveals
/// whether an address has an account.
/// </summary>
public sealed class EmailAuthService(
    ITurnstileVerifier turnstile,
    IEmailSender emailSender,
    SecretHasher hasher,
    OtpLimiter limiter,
    HostLookup hostLookup,
    LoginTokenService loginTokens,
    HostSessions sessions,
    UserManager<Host> userManager,
    IOptions<SpaCorsOptions> spaOptions,
    IClock clock)
{
    private const string UniqueViolation = "23505";
    private static readonly string[] EmailLoginPurposes = [LoginTokenPurposes.EmailLogin, LoginTokenPurposes.RecycleCheck];

    public async Task<IResult> StartLoginAsync(StartEmailLoginRequest request, ClientIp clientIp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(clientIp);
        var isValid = AuthEmails.TryParse(request.Email, out var email);
        var normalizedEmail = userManager.NormalizeEmail(email);
        var target = EmailTarget(normalizedEmail, clientIp);

        if (!await turnstile.VerifyAsync(request.TurnstileToken, TurnstileActions.EmailStart, clientIp.Address, cancellationToken))
        {
            await limiter.LogAsync(target, OtpKinds.Send, OtpOutcomes.TurnstileFailed, cancellationToken);
            return AuthProblems.TurnstileFailed();
        }

        if (!isValid)
        {
            return AuthProblems.InvalidEmail();
        }

        if (await limiter.TryReserveSendAsync(target, cancellationToken) is not { } sendId)
        {
            return AuthProblems.RateLimited();
        }

        var host = await hostLookup.FindByConfirmedEmailAsync(normalizedEmail, cancellationToken);
        if (host is null || host.Status == HostStatus.Banned)
        {
            await limiter.SetOutcomeAsync(sendId, host is null ? OtpOutcomes.Failed : OtpOutcomes.RejectedBanned, cancellationToken);
            return TypedResults.Accepted((string?)null, new EmailLoginStarted(Guid.CreateVersion7()));
        }

        var token = await loginTokens.IssueAsync(LoginTokenPurposes.EmailLogin, host.Id, host.Email!, cancellationToken);
        if (!await emailSender.SendAsync(AuthEmails.Login(host.Email!, token, spaOptions.Value.SpaOrigin), cancellationToken))
        {
            await limiter.SetOutcomeAsync(sendId, OtpOutcomes.Failed, cancellationToken);
        }

        return TypedResults.Accepted((string?)null, new EmailLoginStarted(token.Id));
    }

    public async Task<IResult> CompleteLoginAsync(CompleteEmailLoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var redeemed = request switch
        {
            { Token: { Length: > 0 } link } => await loginTokens.RedeemLinkAsync(LoginTokenPurposes.EmailLogin, link, hostId: null, cancellationToken),
            { LoginId: { } loginId, Code: { Length: > 0 } code } => await loginTokens.RedeemCodeAsync(loginId, EmailLoginPurposes, code, cancellationToken),
            _ => null,
        };
        if (redeemed is null || await userManager.FindByIdAsync(redeemed.HostId.ToString()) is not { } host)
        {
            return AuthProblems.InvalidCode();
        }

        if (host.Status == HostStatus.Banned)
        {
            return AuthProblems.Banned();
        }

        if (!host.EmailConfirmed || host.NormalizedEmail != userManager.NormalizeEmail(redeemed.Email))
        {
            return AuthProblems.InvalidCode();
        }

        return await sessions.SignInAsync(host);
    }

    public async Task<IResult> AddAsync(AddEmailRequest request, ClaimsPrincipal user, ClientIp clientIp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(clientIp);
        if (await userManager.GetUserAsync(user) is not { } host)
        {
            return TypedResults.Unauthorized();
        }

        if (!AuthEmails.TryParse(request.Email, out var email))
        {
            return AuthProblems.InvalidEmail();
        }

        if (await limiter.TryReserveSendAsync(EmailTarget(userManager.NormalizeEmail(email), clientIp), cancellationToken) is not { } sendId)
        {
            return AuthProblems.RateLimited();
        }

        var token = await loginTokens.IssueAsync(LoginTokenPurposes.EmailConfirm, host.Id, email, cancellationToken);
        if (!await emailSender.SendAsync(AuthEmails.Confirm(email, token, spaOptions.Value.SpaOrigin), cancellationToken))
        {
            await limiter.SetOutcomeAsync(sendId, OtpOutcomes.Failed, cancellationToken);
            return AuthProblems.EmailUnavailable();
        }

        return TypedResults.Accepted((string?)null);
    }

    public async Task<IResult> ConfirmAsync(ConfirmEmailRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await userManager.GetUserAsync(user) is not { } host)
        {
            return TypedResults.Unauthorized();
        }

        var redeemed = request switch
        {
            { Token: { Length: > 0 } link } => await loginTokens.RedeemLinkAsync(LoginTokenPurposes.EmailConfirm, link, host.Id, cancellationToken),
            { Code: { Length: > 0 } code } => await loginTokens.RedeemHostCodeAsync(host.Id, LoginTokenPurposes.EmailConfirm, code, cancellationToken),
            _ => null,
        };
        if (redeemed is null)
        {
            return AuthProblems.InvalidCode();
        }

        var normalizedEmail = userManager.NormalizeEmail(redeemed.Email);
        if (await hostLookup.FindByConfirmedEmailAsync(normalizedEmail, cancellationToken) is { } owner && owner.Id != host.Id)
        {
            return AuthProblems.EmailInUse();
        }

        host.Email = redeemed.Email;
        host.NormalizedEmail = normalizedEmail;
        host.EmailConfirmed = true;
        host.EmailVerifiedAt = clock.GetCurrentInstant().ToDateTimeOffset();
        try
        {
            HostSessions.EnsureSucceeded(await userManager.UpdateSecurityStampAsync(host));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            return AuthProblems.EmailInUse();
        }

        await sessions.RefreshAsync(host);
        return TypedResults.Ok(MeResponse.From(host));
    }

    public async Task<IResult> RemoveAsync(ClaimsPrincipal user)
    {
        if (await userManager.GetUserAsync(user) is not { } host)
        {
            return TypedResults.Unauthorized();
        }

        host.Email = null;
        host.NormalizedEmail = null;
        host.EmailConfirmed = false;
        host.EmailVerifiedAt = null;
        HostSessions.EnsureSucceeded(await userManager.UpdateSecurityStampAsync(host));
        await sessions.RefreshAsync(host);
        return TypedResults.Ok(MeResponse.From(host));
    }

    private OtpTarget EmailTarget(string normalizedEmail, ClientIp clientIp) =>
        new(OtpChannels.Email, hasher.HashEmail(normalizedEmail), hasher.HashIpKey(clientIp.KeyHex));
}
