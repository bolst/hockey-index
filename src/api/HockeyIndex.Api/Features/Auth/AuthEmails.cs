using System.Net.Mail;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Integrations.Email;

namespace HockeyIndex.Api.Features.Auth;

/// <summary>
/// Sign-in emails. Magic links carry the token in the URL fragment so it never reaches servers or mail-scanner GETs;
/// the SPA page POSTs it after a click (plan R15).
/// </summary>
internal static class AuthEmails
{
    private const int MaxEmailLength = 254;

    public static bool TryParse(string? input, out string email)
    {
        email = input?.Trim() ?? "";
        return email.Length is > 0 and <= MaxEmailLength
            && MailAddress.TryCreate(email, out var address)
            && address.Address == email
            && address.Host.Contains('.', StringComparison.Ordinal);
    }

    public static EmailMessage Confirm(string to, IssuedLoginToken token, string spaOrigin) => new(
        to,
        "Confirm your email for Hockey Index",
        $"""
        Your Hockey Index confirmation code is {token.Code}.

        Or confirm with this link: {spaOrigin}/auth/email/confirm#t={token.LinkToken}

        The code expires in 10 minutes. If you did not add this email, ignore this message.
        """);

    public static EmailMessage Login(string to, IssuedLoginToken token, string spaOrigin) => new(
        to,
        "Your Hockey Index sign-in code",
        $"""
        Your Hockey Index sign-in code is {token.Code}.

        Or sign in with this link: {spaOrigin}/auth/email/login#t={token.LinkToken}

        The code expires in 10 minutes. If you did not try to sign in, ignore this message.
        """);

    public static EmailMessage RecycleCheck(string to, IssuedLoginToken token) => new(
        to,
        "Confirm it's you on Hockey Index",
        $"""
        Someone verified your phone number on Hockey Index. To finish signing in, enter this code: {token.Code}.

        The code expires in 10 minutes. If this was not you, ignore this message.
        """);

    public static string Hint(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }
}
