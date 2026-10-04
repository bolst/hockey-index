namespace HockeyIndex.Api.Domain;

public sealed class EventStatusChange
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid EventId { get; init; }

    /// <summary>Denormalized for the daily publish cap.</summary>
    public required Guid HostId { get; init; }

    public required EventStatus FromStatus { get; init; }
    public required EventStatus ToStatus { get; init; }

    /// <summary>Null for system actions (jobs).</summary>
    public Guid? ActorId { get; init; }

    public string? Reason { get; init; }
    public required DateTimeOffset At { get; init; }
}
