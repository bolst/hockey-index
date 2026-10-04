using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using HockeyIndex.Api.Integrations.Email;
using HockeyIndex.Api.Integrations.Turnstile;
using HockeyIndex.Api.Integrations.Twilio;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using Npgsql;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Features.Auth;

/// <summary>
/// SMS sign-in. Start runs every gate in plan order (Turnstile, region, blocklist, banned, sign-up mode, limiter,
/// breaker, budget, Lookup) before Twilio Verify; each refusal is logged to <c>otp_log</c> and costs no Twilio call.
/// </summary>
public sealed partial class PhoneOtpService(
    AppDbContext db,
    ITurnstileVerifier turnstile,
    ISmsVerifier sms,
    IPhoneLineTypeLookup lineTypeLookup,
    IEmailSender emailSender,
    SecretHasher hasher,
    OtpLimiter limiter,
    BreakerService breakers,
    ProviderBudget budget,
    HostLookup hostLookup,
    LoginTokenService loginTokens,
    HostSessions sessions,
    UserManager<Host> userManager,
    IOptions<SignupOptions> signupOptions,
    IOptions<OtpOptions> otpOptions,
    IClock clock,
    HockeyIndexMetrics metrics,
    ILogger<PhoneOtpService> logger)
{
    private const string UniqueViolation = "23505";

    public async Task<IResult> StartAsync(StartPhoneOtpRequest request, ClientIp clientIp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(clientIp);
        var normalization = PhoneNormalizer.Normalize(request.Phone);
        var target = SmsTarget(normalization, request.Phone, clientIp);

        if (!await turnstile.VerifyAsync(request.TurnstileToken, TurnstileActions.PhoneStart, clientIp.Address, cancellationToken))
        {
            return await RefuseAsync(target, OtpOutcomes.TurnstileFailed, AuthProblems.TurnstileFailed(), cancellationToken);
        }

        switch (normalization.Rejection)
        {
            case PhoneRejection.Invalid:
                return await RefuseAsync(target, OtpOutcomes.RejectedRegion, AuthProblems.InvalidPhone(), cancellationToken);
            case PhoneRejection.Region:
                return await RefuseAsync(target, OtpOutcomes.RejectedRegion, AuthProblems.UnsupportedRegion(), cancellationToken);
            case PhoneRejection.TollFree:
                return await RefuseAsync(target, OtpOutcomes.RejectedLineType, AuthProblems.UnsupportedLineType(), cancellationToken);
        }

        var e164 = normalization.Phone!.E164;
        if (await db.Blocklist.AnyAsync(entry => entry.Kind == BlocklistKind.Phone && entry.Value == e164, cancellationToken))
        {
            return await RefuseAsync(target, OtpOutcomes.RejectedBlocklist, TypedResults.Accepted((string?)null), cancellationToken);
        }

        var host = await hostLookup.FindByPhoneAsync(e164, cancellationToken);
        if (host is { Status: HostStatus.Banned })
        {
            return await RefuseAsync(target, OtpOutcomes.RejectedBanned, TypedResults.Accepted((string?)null), cancellationToken);
        }

        if (host is null && signupOptions.Value.IsInviteOnly && !await HasLiveInviteAsync(e164, cancellationToken))
        {
            return await RefuseAsync(target, OtpOutcomes.InviteRequired, AuthProblems.InviteRequired(), cancellationToken);
        }

        if (await limiter.TryReserveSendAsync(target, cancellationToken) is not { } sendId)
        {
            metrics.RecordOtpSend(OtpOutcomes.Limited);
            return AuthProblems.RateLimited();
        }

        return await SendAsync(sendId, target, e164, host?.Id, cancellationToken);
    }

    public async Task<IResult> VerifyAsync(VerifyPhoneOtpRequest request, ClientIp clientIp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(clientIp);
        var normalization = PhoneNormalizer.Normalize(request.Phone);
        if (!normalization.IsValid || request.Code is not { Length: >= 4 and <= 10 } code || !code.All(char.IsAsciiDigit))
        {
            return AuthProblems.InvalidCode();
        }

        var e164 = normalization.Phone!.E164;
        var target = SmsTarget(normalization, request.Phone, clientIp);
        if (await limiter.TryReserveCheckAsync(target, cancellationToken) is not { } checkId)
        {
            metrics.RecordOtpVerify(OtpOutcomes.Limited);
            return AuthProblems.RateLimited();
        }

        if (!await sms.CheckAsync(e164, code, cancellationToken))
        {
            metrics.RecordOtpVerify(OtpOutcomes.Failed);
            return AuthProblems.InvalidCode();
        }

        await limiter.SetOutcomeAsync(checkId, OtpOutcomes.Verified, cancellationToken);
        metrics.RecordOtpVerify(OtpOutcomes.Verified);

        var now = Now();
        var host = await hostLookup.FindByPhoneAsync(e164, cancellationToken);
        var isNewHost = host is null;
        host ??= await CreateHostAsync(e164, now, cancellationToken);
        if (host is null)
        {
            return AuthProblems.InviteRequired();
        }

        if (host.Status == HostStatus.Banned)
        {
            return AuthProblems.Banned();
        }

        var lineType = await limiter.FindCachedLineTypeAsync(target.TargetHash, cancellationToken);
        host.PhoneVerifiedAt = now;
        host.PhoneNumberConfirmed = true;
        if (lineType is not null)
        {
            host.PhoneLineType = lineType;
            host.IsVoip = PhoneLineTypes.IsVoip(lineType);
        }

        if (!isNewHost && MayBeRecycledNumber(host, now) && host.Email is { } email)
        {
            HostSessions.EnsureSucceeded(await userManager.UpdateAsync(host));
            var token = await loginTokens.IssueAsync(LoginTokenPurposes.RecycleCheck, host.Id, email, cancellationToken);
            await emailSender.SendAsync(AuthEmails.RecycleCheck(email, token), cancellationToken);
            return TypedResults.Accepted((string?)null, new EmailCheckRequired(token.Id, AuthEmails.Hint(email)));
        }

        return await sessions.SignInAsync(host);
    }

    private async Task<IResult> SendAsync(Guid sendId, OtpTarget target, string e164, Guid? hostId, CancellationToken cancellationToken)
    {
        if (await breakers.IsOpenAsync(BreakerNames.OtpSms, cancellationToken))
        {
            return await MarkAsync(sendId, OtpOutcomes.BreakerOpen, AuthProblems.SmsUnavailable(), cancellationToken);
        }

        var lineType = await limiter.FindCachedLineTypeAsync(target.TargetHash, cancellationToken);
        var needsLookup = lineType is null;
        if ((needsLookup && !await budget.TryConsumeAsync(ProviderNames.TwilioLookup, hostId, cancellationToken))
            || !await budget.TryConsumeAsync(ProviderNames.TwilioVerify, hostId, cancellationToken))
        {
            return await MarkAsync(sendId, OtpOutcomes.BudgetExhausted, AuthProblems.SmsUnavailable(), cancellationToken);
        }

        if (needsLookup && await lineTypeLookup.LookupLineTypeAsync(e164, cancellationToken) is { } lookedUp)
        {
            lineType = lookedUp;
            await limiter.SetLineTypeAsync(sendId, lookedUp, cancellationToken);
        }

        if (lineType is not null && PhoneLineTypes.IsRejected(lineType))
        {
            return await MarkAsync(sendId, OtpOutcomes.RejectedLineType, AuthProblems.UnsupportedLineType(), cancellationToken);
        }

        if (lineType is not null && PhoneLineTypes.IsVoip(lineType))
        {
            LogVoipSend(logger, lineType);
        }

        if (!await sms.StartAsync(e164, cancellationToken))
        {
            return await MarkAsync(sendId, OtpOutcomes.Failed, AuthProblems.SmsUnavailable(), cancellationToken);
        }

        metrics.RecordOtpSend(OtpOutcomes.Sent);
        await TripBreakerIfNeededAsync(cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private async Task TripBreakerIfNeededAsync(CancellationToken cancellationToken)
    {
        var (sends, verified) = await limiter.CountSmsLastHourAsync(cancellationToken);
        metrics.SetOtpConversion(sends, verified);
        var options = otpOptions.Value;
        if (OtpBreakerRules.EvaluateLastHour(sends, verified, options) is not { } reason
            || await breakers.IsOpenAsync(BreakerNames.OtpSms, cancellationToken))
        {
            return;
        }

        await breakers.OpenAsync(BreakerNames.OtpSms, Now().AddMinutes(options.BreakerOpenMinutes), reason, BreakerOpener.Auto, cancellationToken);
        LogBreakerOpened(logger, reason);
    }

    /// <returns>The new host, the host a concurrent request created, or <c>null</c> when sign-up needs an invite.</returns>
    private async Task<Host?> CreateHostAsync(string e164, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (signupOptions.Value.IsInviteOnly && !await ClaimInviteAsync(e164, now, cancellationToken))
        {
            return await hostLookup.FindByPhoneAsync(e164, cancellationToken);
        }

        var host = new Host
        {
            Id = Guid.CreateVersion7(),
            UserName = e164,
            PhoneNumber = e164,
            PhoneNumberConfirmed = true,
            PhoneVerifiedAt = now,
            CreatedAt = now,
        };

        try
        {
            if ((await userManager.CreateAsync(host)).Succeeded)
            {
                return host;
            }
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            db.ChangeTracker.Clear();
        }

        return await hostLookup.FindByPhoneAsync(e164, cancellationToken)
            ?? throw new InvalidOperationException("Host creation failed without a conflicting host.");
    }

    private Task<bool> HasLiveInviteAsync(string e164, CancellationToken cancellationToken)
    {
        var now = Now();
        return db.SignupInvites.AnyAsync(invite => invite.PhoneE164 == e164 && invite.UsedAt == null && invite.ExpiresAt > now, cancellationToken);
    }

    private async Task<bool> ClaimInviteAsync(string e164, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.SignupInvites
            .Where(invite => invite.PhoneE164 == e164 && invite.UsedAt == null && invite.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(invite => invite.UsedAt, now), cancellationToken) > 0;

    private bool MayBeRecycledNumber(Host host, DateTimeOffset now) =>
        host.EmailConfirmed && (host.LastLoginAt ?? host.CreatedAt) < now.AddDays(-otpOptions.Value.RecycledNumberAfterDays);

    private OtpTarget SmsTarget(PhoneNormalization normalization, string? rawPhone, ClientIp clientIp) => new(
        OtpChannels.Sms,
        hasher.HashPhone(normalization.Phone?.E164 ?? rawPhone?.Trim() ?? ""),
        hasher.HashIpKey(clientIp.KeyHex),
        normalization.IsValid ? normalization.Phone!.NpaNxx : null);

    private async Task<IResult> RefuseAsync(OtpTarget target, string outcome, IResult response, CancellationToken cancellationToken)
    {
        await limiter.LogAsync(target, OtpKinds.Send, outcome, cancellationToken);
        metrics.RecordOtpSend(outcome);
        return response;
    }

    private async Task<IResult> MarkAsync(Guid sendId, string outcome, IResult response, CancellationToken cancellationToken)
    {
        await limiter.SetOutcomeAsync(sendId, outcome, cancellationToken);
        metrics.RecordOtpSend(outcome);
        return response;
    }

    private DateTimeOffset Now() => clock.GetCurrentInstant().ToDateTimeOffset();

    [LoggerMessage(Level = LogLevel.Information, Message = "OTP sent to a VoIP number ({LineType})")]
    private static partial void LogVoipSend(ILogger logger, string lineType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "otp_sms breaker opened: {Reason}")]
    private static partial void LogBreakerOpened(ILogger logger, string reason);
}
