using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HockeyIndex.Api.Infrastructure.Observability;

public sealed record HealthStatus(string Status);

public static class HealthChecks
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => TypedResults.Ok(new HealthStatus("ok")))
            .RequireRateLimiting(RateLimitPolicies.PublicRead);

        app.MapGet("/readyz", CheckReadinessAsync)
            .AddEndpointFilter(OpsListenerOnly);

        return app;
    }

    private static async Task<IResult> CheckReadinessAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database unreachable");
        }

        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pendingMigrations.Count > 0)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Pending migrations",
                extensions: new Dictionary<string, object?> { ["pending"] = pendingMigrations });
        }

        return TypedResults.Ok(new HealthStatus("ready"));
    }

    private static ValueTask<object?> OpsListenerOnly(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.IsOpsListener() ? next(context) : ValueTask.FromResult<object?>(TypedResults.NotFound());
}
