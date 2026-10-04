using HockeyIndex.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HockeyIndex.Api.Features.Safety;

/// <param name="Reason">One of <c>scam</c>, <c>spam</c>, <c>offensive</c>, <c>inaccurate</c>, <c>other</c>.</param>
/// <param name="Details">Optional free text, at most 500 characters.</param>
public sealed record ReportRequest(string? Reason, string? Details, string? TurnstileToken);

public static class OutboundLinkStatus
{
    /// <summary><c>u</c> matches a safe scan of the visible event <c>e</c>; the interstitial MAY offer the link.</summary>
    public const string Ok = "ok";

    /// <summary>Unknown URL, unknown or hidden event, or no safe scan: the interstitial MUST NOT link to <c>u</c>.</summary>
    public const string Unrecognized = "unrecognized";
}

/// <param name="Url">Canonical URL to open (only when <see cref="Status"/> is <c>ok</c>).</param>
/// <param name="Host">Host of <paramref name="Url"/> in Unicode form (only when <c>ok</c>).</param>
/// <param name="FinalHost">Host the link resolved to after shortener expansion, Unicode form (only when <c>ok</c>).</param>
public sealed record OutboundLinkResponse(string Status, string? Url = null, string? Host = null, string? FinalHost = null);

/// <summary>Public visibility rule shared by reports and <c>/out/check</c> (plan 4.1).</summary>
public static class EventVisibility
{
    public static bool IsPublic(HockeyEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return evt.Status is EventStatus.Published or EventStatus.Cancelled
            || (evt.Status == EventStatus.Archived && evt.HiddenReason is null);
    }
}

internal static class SafetyProblems
{
    public static IResult InvalidReport(string detail) => Of(StatusCodes.Status422UnprocessableEntity, "invalid_report", detail);
    public static IResult ReportLimitReached() => Of(StatusCodes.Status429TooManyRequests, "report_limit_reached", "You have sent too many reports. Try again later.");
    public static IResult InvalidSignature() => Of(StatusCodes.Status403Forbidden, "invalid_signature", "Signature check failed.");

    private static ProblemHttpResult Of(int status, string code, string title) =>
        TypedResults.Problem(title: title, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
