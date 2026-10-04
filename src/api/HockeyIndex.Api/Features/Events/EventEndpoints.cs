using System.Security.Claims;
using HockeyIndex.Api.Features.Admin;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using HockeyIndex.Api.Infrastructure.Time;

namespace HockeyIndex.Api.Features.Events;

public static class EventEndpoints
{
    public static IServiceCollection AddEventsFeature(this IServiceCollection services)
    {
        services.AddSingleton<VenueTimeConverter>();
        services.AddScoped<AuditRecorder>();
        services.AddScoped<EventTransitions>();
        services.AddScoped<EventValidator>();
        services.AddScoped<EventResponder>();
        services.AddScoped<LinkScanRecorder>();
        services.AddScoped<CreateDraft>();
        services.AddScoped<UpdateEvent>();
        services.AddScoped<PublishEvent>();
        services.AddScoped<CancelEvent>();
        services.AddScoped<DuplicateEvent>();
        services.AddScoped<DeleteDraft>();
        services.AddScoped<ListMyEvents>();
        services.AddScoped<GetMyEvent>();
        return services;
    }

    public static void Map(RouteGroupBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);

        var events = v1.MapGroup("/host/events").RequireAuthorization().RequireRateLimiting(RateLimitPolicies.HostWrite);
        events.MapGet("", (string? status, ClaimsPrincipal user, ListMyEvents list, CancellationToken ct) =>
            AsHost(user, hostId => list.HandleAsync(status, hostId, ct)));
        events.MapPost("", (EventRequest request, ClaimsPrincipal user, HttpResponse response, CreateDraft create, CancellationToken ct) =>
            AsHost(user, hostId => create.HandleAsync(request, hostId, response, ct)));
        events.MapGet("/{publicId}", (string publicId, ClaimsPrincipal user, HttpResponse response, GetMyEvent get, CancellationToken ct) =>
            AsHost(user, hostId => get.HandleAsync(publicId, hostId, response, ct)));
        events.MapPut("/{publicId}", (string publicId, EventRequest request, ClaimsPrincipal user, HttpRequest httpRequest, HttpResponse response, UpdateEvent update, CancellationToken ct) =>
            AsHost(user, hostId => update.HandleAsync(publicId, request, hostId, httpRequest, response, ct)));
        events.MapDelete("/{publicId}", (string publicId, ClaimsPrincipal user, DeleteDraft delete, CancellationToken ct) =>
            AsHost(user, hostId => delete.HandleAsync(publicId, hostId, ct)));
        events.MapPost("/{publicId}/publish", (string publicId, ClaimsPrincipal user, HttpResponse response, PublishEvent publish, CancellationToken ct) =>
            AsHost(user, hostId => publish.HandleAsync(publicId, hostId, response, ct)));
        events.MapPost("/{publicId}/cancel", (string publicId, ClaimsPrincipal user, HttpResponse response, CancelEvent cancel, CancellationToken ct) =>
            AsHost(user, hostId => cancel.HandleAsync(publicId, hostId, response, ct)));
        events.MapPost("/{publicId}/duplicate", (string publicId, ClaimsPrincipal user, HttpResponse response, DuplicateEvent duplicate, CancellationToken ct) =>
            AsHost(user, hostId => duplicate.HandleAsync(publicId, hostId, response, ct)));
    }

    private static Task<IResult> AsHost(ClaimsPrincipal user, Func<Guid, Task<IResult>> handle) =>
        user.GetHostId() is { } hostId ? handle(hostId) : Task.FromResult<IResult>(TypedResults.Unauthorized());
}
