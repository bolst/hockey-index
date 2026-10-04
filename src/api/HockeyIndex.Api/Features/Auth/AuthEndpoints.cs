using System.Security.Claims;
using HockeyIndex.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Identity;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IServiceCollection AddAuthFeature(this IServiceCollection services)
    {
        services.AddScoped<HostSessions>();
        services.AddScoped<PhoneOtpService>();
        services.AddScoped<EmailAuthService>();
        return services;
    }

    public static void Map(RouteGroupBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);

        var auth = v1.MapGroup("/auth");
        auth.MapPost("/phone/start", (StartPhoneOtpRequest request, HttpContext http, PhoneOtpService phone, CancellationToken ct) =>
            phone.StartAsync(request, http.GetClientIp(), ct));
        auth.MapPost("/phone/verify", (VerifyPhoneOtpRequest request, HttpContext http, PhoneOtpService phone, CancellationToken ct) =>
            phone.VerifyAsync(request, http.GetClientIp(), ct));
        auth.MapPost("/email/start", (StartEmailLoginRequest request, HttpContext http, EmailAuthService email, CancellationToken ct) =>
            email.StartLoginAsync(request, http.GetClientIp(), ct));
        auth.MapPost("/email/complete", (CompleteEmailLoginRequest request, EmailAuthService email, CancellationToken ct) =>
            email.CompleteLoginAsync(request, ct));
        auth.MapPost("/signout", async (SignInManager<Host> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.NoContent();
        });

        var me = v1.MapGroup("/me").RequireAuthorization();
        me.MapGet("", GetMeAsync);
        me.MapPost("/email", (AddEmailRequest request, ClaimsPrincipal user, HttpContext http, EmailAuthService email, CancellationToken ct) =>
            email.AddAsync(request, user, http.GetClientIp(), ct));
        me.MapPost("/email/confirm", (ConfirmEmailRequest request, ClaimsPrincipal user, EmailAuthService email, CancellationToken ct) =>
            email.ConfirmAsync(request, user, ct));
        me.MapDelete("/email", (ClaimsPrincipal user, EmailAuthService email) => email.RemoveAsync(user));
    }

    private static async Task<IResult> GetMeAsync(ClaimsPrincipal user, UserManager<Host> userManager) =>
        await userManager.GetUserAsync(user) is { } host ? TypedResults.Ok(MeResponse.From(host)) : TypedResults.Unauthorized();
}
