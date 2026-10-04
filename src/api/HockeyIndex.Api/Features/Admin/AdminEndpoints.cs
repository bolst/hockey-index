using System.Security.Claims;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HockeyIndex.Api.Features.Admin;

/// <summary>Signed in, <c>is_admin</c> and active, checked against the database on every request (not a cookie claim).</summary>
public sealed class AdminRequirement : IAuthorizationRequirement;

public sealed class AdminRequirementHandler(AppDbContext db) : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.User.GetHostId() is { } hostId
            && await db.Users.AsNoTracking().AnyAsync(host => host.Id == hostId && host.IsAdmin && host.Status == HostStatus.Active))
        {
            context.Succeed(requirement);
        }
    }
}

public static class AdminEndpoints
{
    public const string Policy = "admin";

    public static IServiceCollection AddAdminFeature(this IServiceCollection services)
    {
        services.AddOptions<AdminOptions>().BindConfiguration(AdminOptions.SectionName);
        services.AddScoped<IAuthorizationHandler, AdminRequirementHandler>();
        services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));
        services.AddScoped<ModerateEvents>();
        services.AddScoped<ManageHosts>();
        services.AddScoped<ManageBlocklist>();
        services.AddScoped<ManageInvites>();
        services.AddScoped<ManageBreakers>();
        services.AddScoped<ManageVenues>();
        services.AddScoped<ListAudit>();
        return services;
    }

    public static void Map(RouteGroupBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);

        var admin = v1.MapGroup("/admin").RequireAuthorization(Policy).RequireRateLimiting(RateLimitPolicies.Admin);

        admin.MapGet("/queue", async (ModerateEvents moderate, CancellationToken ct) => TypedResults.Ok(await moderate.QueueAsync(ct)));
        admin.MapPost("/events/{publicId}/hide", (string publicId, AdminReasonRequest body, ClaimsPrincipal user, ModerateEvents moderate, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => moderate.HideAsync(publicId, reason, adminId, ct)));
        admin.MapPost("/events/{publicId}/restore", (string publicId, AdminReasonRequest body, ClaimsPrincipal user, ModerateEvents moderate, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => moderate.RestoreAsync(publicId, reason, adminId, ct)));
        admin.MapPost("/events/{publicId}/clear-hidden-reason", (string publicId, AdminReasonRequest body, ClaimsPrincipal user, ModerateEvents moderate, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => moderate.ClearHiddenReasonAsync(publicId, reason, adminId, ct)));

        admin.MapGet("/hosts/{hostId:guid}", (Guid hostId, ManageHosts hosts, CancellationToken ct) => hosts.GetAsync(hostId, ct));
        admin.MapPost("/hosts/{hostId:guid}/ban", (Guid hostId, AdminReasonRequest body, ClaimsPrincipal user, ManageHosts hosts, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => hosts.BanAsync(hostId, reason, adminId, ct)));
        admin.MapPost("/hosts/{hostId:guid}/unban", (Guid hostId, AdminReasonRequest body, ClaimsPrincipal user, ManageHosts hosts, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => hosts.UnbanAsync(hostId, reason, adminId, ct)));

        admin.MapGet("/blocklist", async (ManageBlocklist blocklist, CancellationToken ct) => TypedResults.Ok(await blocklist.ListAsync(ct)));
        admin.MapPost("/blocklist", (BlocklistAddRequest body, ClaimsPrincipal user, ManageBlocklist blocklist, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => blocklist.AddAsync(body, reason, adminId, ct)));
        admin.MapDelete("/blocklist/{id:guid}", (Guid id, [FromBody] AdminReasonRequest body, ClaimsPrincipal user, ManageBlocklist blocklist, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => blocklist.RemoveAsync(id, reason, adminId, ct)));

        admin.MapGet("/invites", async (ManageInvites invites, CancellationToken ct) => TypedResults.Ok(await invites.ListAsync(ct)));
        admin.MapPost("/invites", (InviteCreateRequest body, ClaimsPrincipal user, ManageInvites invites, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => invites.CreateAsync(body, reason, adminId, ct)));
        admin.MapDelete("/invites/{id:guid}", (Guid id, [FromBody] AdminReasonRequest body, ClaimsPrincipal user, ManageInvites invites, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => invites.RevokeAsync(id, reason, adminId, ct)));

        admin.MapGet("/breakers", async (ManageBreakers breakers, CancellationToken ct) => TypedResults.Ok(await breakers.ListAsync(ct)));
        admin.MapPost("/breakers/{name}", (string name, BreakerChangeRequest body, ClaimsPrincipal user, ManageBreakers breakers, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => breakers.ChangeAsync(name, body, reason, adminId, ct)));

        admin.MapGet("/venues", async (string? q, ManageVenues venues, CancellationToken ct) => TypedResults.Ok(await venues.SearchAsync(q, ct)));
        admin.MapPut("/venues/{publicId}", (string publicId, VenueEditRequest body, ClaimsPrincipal user, ManageVenues venues, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => venues.UpdateAsync(publicId, body, reason, adminId, ct)));
        admin.MapPost("/venues/{publicId}/merge", (string publicId, VenueMergeRequest body, ClaimsPrincipal user, ManageVenues venues, CancellationToken ct) =>
            AsAdmin(user, body.Reason, (adminId, reason) => venues.MergeAsync(publicId, body, reason, adminId, ct)));

        admin.MapGet("/audit", async (long? cursor, int? limit, ListAudit audit, CancellationToken ct) =>
            TypedResults.Ok(await audit.HandleAsync(cursor, limit, ct)));
    }

    private static Task<IResult> AsAdmin(ClaimsPrincipal user, string? rawReason, Func<Guid, string, Task<IResult>> handle)
    {
        if (user.GetHostId() is not { } adminId)
        {
            return Task.FromResult<IResult>(TypedResults.Unauthorized());
        }

        return AdminReason.Validate(rawReason) is { } reason
            ? handle(adminId, reason)
            : Task.FromResult(AdminProblems.ReasonRequired());
    }
}

/// <summary>Newest first; keyset paging on <c>id</c>.</summary>
public sealed class ListAudit(AppDbContext db)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public async Task<AuditPage> HandleAsync(long? cursor, int? limit, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var query = db.AuditLog.AsNoTracking();
        if (cursor is { } before)
        {
            query = query.Where(entry => entry.Id < before);
        }

        var rows = await query.OrderByDescending(entry => entry.Id).Take(take + 1).ToListAsync(cancellationToken);
        var page = rows.Take(take).Select(AuditEntryResponse.From).ToList();
        return new AuditPage(page, rows.Count > take ? page[^1].Id : null);
    }
}
