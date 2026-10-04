using Microsoft.AspNetCore.Http.HttpResults;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Features.Auth;

public sealed record StartPhoneOtpRequest(string? Phone, string? TurnstileToken);

public sealed record VerifyPhoneOtpRequest(string? Phone, string? Code);

public sealed record StartEmailLoginRequest(string? Email, string? TurnstileToken);

/// <summary>Either <see cref="LoginId"/> with <see cref="Code"/>, or a magic-link <see cref="Token"/>.</summary>
public sealed record CompleteEmailLoginRequest(Guid? LoginId, string? Code, string? Token);

public sealed record AddEmailRequest(string? Email);

/// <summary>Either the emailed <see cref="Code"/> or the magic-link <see cref="Token"/>.</summary>
public sealed record ConfirmEmailRequest(string? Code, string? Token);

public sealed record EmailLoginStarted(Guid LoginId);

/// <summary>The phone was verified but the account may belong to a previous owner of the number; finish with the emailed code.</summary>
public sealed record EmailCheckRequired(Guid LoginId, string EmailHint);

public sealed record MeResponse(Guid Id, string Phone, string? Email, bool IsVoip, bool IsAdmin, DateTimeOffset CreatedAt)
{
    public static MeResponse From(Host host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new(host.Id, host.PhoneNumber!, host.EmailConfirmed ? host.Email : null, host.IsVoip, host.IsAdmin, host.CreatedAt);
    }
}

internal static class AuthProblems
{
    public static IResult TurnstileFailed() => Of(StatusCodes.Status400BadRequest, "turnstile_failed", "Human verification failed.");
    public static IResult InvalidPhone() => Of(StatusCodes.Status422UnprocessableEntity, "invalid_phone", "Enter a valid phone number.");
    public static IResult UnsupportedRegion() => Of(StatusCodes.Status422UnprocessableEntity, "unsupported_region", "Only US and Canadian numbers are supported.");
    public static IResult UnsupportedLineType() => Of(StatusCodes.Status422UnprocessableEntity, "unsupported_line_type", "This number cannot receive text messages. Use a mobile number.");
    public static IResult InviteRequired() => Of(StatusCodes.Status403Forbidden, "invite_required", "Sign-up is currently by invitation only.");
    public static IResult RateLimited() => Of(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many attempts. Try again later.");
    public static IResult SmsUnavailable() => Of(StatusCodes.Status503ServiceUnavailable, "sms_unavailable", "Text-message sign-in is temporarily unavailable.");
    public static IResult EmailUnavailable() => Of(StatusCodes.Status503ServiceUnavailable, "email_unavailable", "Email is temporarily unavailable.");
    public static IResult InvalidCode() => Of(StatusCodes.Status400BadRequest, "invalid_code", "The code is invalid or has expired.");
    public static IResult InvalidEmail() => Of(StatusCodes.Status400BadRequest, "invalid_email", "Enter a valid email address.");
    public static IResult EmailInUse() => Of(StatusCodes.Status409Conflict, "email_in_use", "This email is already linked to another account.");
    public static IResult Banned() => Of(StatusCodes.Status403Forbidden, "account_banned", "This account is suspended.");

    private static ProblemHttpResult Of(int status, string code, string title) =>
        TypedResults.Problem(title: title, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
