using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Infrastructure.Auth;

public sealed class SignupOptions
{
    public const string SectionName = "Signup";
    public const string Open = "open";
    public const string InviteOnly = "invite_only";

    /// <summary><c>open</c> or <c>invite_only</c> (unknown phones need an unexpired, unused <c>signup_invites</c> row).</summary>
    public string Mode { get; set; } = Open;

    public bool IsInviteOnly => string.Equals(Mode, InviteOnly, StringComparison.OrdinalIgnoreCase);
}

public static class AuthSetup
{
    public const string CookieName = "__Host-hi_auth";
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan SecurityStampInterval = TimeSpan.FromMinutes(1);

    // WebApplication auto-inserts UseAuthentication/UseAuthorization at the top of the pipeline unless these
    // properties are set on the root builder. Our middleware runs inside a UseWhen branch, whose property writes
    // do not reach the root, so they are marked explicitly to keep public paths free of the auth pipeline.
    private const string AuthenticationMiddlewareSetKey = "__AuthenticationMiddlewareSet";
    private const string AuthorizationMiddlewareSetKey = "__AuthorizationMiddlewareSet";

    public static IServiceCollection AddHostAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = CookieLifetime;
                options.SlidingExpiration = true;
                options.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            // SecurityStampValidator signs out of this scheme when it rejects a session; 2FA itself is unused.
            .AddCookie(IdentityConstants.TwoFactorRememberMeScheme, options =>
            {
                options.Cookie.Name = "__Host-hi_2fa";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.Path = "/";
            });
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<TimeProvider>((options, timeProvider) => options.TimeProvider = timeProvider);

        services.AddIdentityCore<Host>(options => options.User.RequireUniqueEmail = false)
            .AddSignInManager()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddUserValidator<HostUserValidator>();

        services.AddScoped<ISecurityStampValidator, HostSecurityStampValidator>();
        services.AddOptions<SecurityStampValidatorOptions>()
            .Configure<TimeProvider>((options, timeProvider) =>
            {
                options.ValidationInterval = SecurityStampInterval;
                options.TimeProvider = timeProvider;
            });

        services.AddOptions<HmacOptions>().BindConfiguration(HmacOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SignupOptions>().BindConfiguration(SignupOptions.SectionName);
        services.AddSingleton<SecretHasher>();
        services.AddScoped<HostLookup>();
        services.AddScoped<LoginTokenService>();
        services.AddAuthorization();
        return services;
    }

    public static IApplicationBuilder UseHostAuthentication(this IApplicationBuilder app)
    {
        app.Properties[AuthenticationMiddlewareSetKey] = true;
        app.Properties[AuthorizationMiddlewareSetKey] = true;

        return app.UseWhen(
            context => !context.IsOpsListener() && !PublicPaths.Match(context),
            branch => branch.UseAuthentication().UseMiddleware<RequestedWithGuard>().UseAuthorization());
    }
}
