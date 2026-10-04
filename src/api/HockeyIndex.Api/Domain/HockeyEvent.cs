using NpgsqlTypes;

namespace HockeyIndex.Api.Domain;

/// <summary>
/// A listing. <see cref="Status"/>, <see cref="HiddenReason"/>, <see cref="HiddenFromStatus"/> and the lifecycle timestamps
/// change only through <see cref="ApplyLifecycle"/>, which only <c>EventTransitions</c> calls (enforced by a unit test).
/// </summary>
public sealed class HockeyEvent
{
    public const int RinkLabelMaxLength = 40;
    public const int TitleMaxLength = 100;
    public const int DescriptionMaxLength = 2000;
    public const int ScheduleTextMaxLength = 500;
    public const int JoinInstructionsMaxLength = 2000;
    public const int MinSkill = 0;
    public const int MaxSkill = 7;
    public const int MaxFeeCents = 100_000;
    public static readonly IReadOnlyList<string> Currencies = ["CAD", "USD"];

    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string PublicId { get; init; }
    public required Guid HostId { get; init; }
    public required Guid VenueId { get; set; }
    public string? RinkLabel { get; set; }
    public required EventType Type { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }

    /// <summary>Wall time at the venue (<c>timestamp without time zone</c>, <see cref="DateTimeKind.Unspecified"/>).</summary>
    public required DateTime StartsLocal { get; set; }

    /// <summary>Wall time at the venue; for leagues and tournaments, 00:00 of the day after the season ends.</summary>
    public required DateTime EndsLocal { get; set; }

    public required DateTimeOffset StartsAt { get; set; }
    public required DateTimeOffset EndsAt { get; set; }
    public string? ScheduleText { get; set; }

    /// <summary>Canonical <c>[min,max+1)</c> within 0–7.</summary>
    public required NpgsqlRange<int> SkillRange { get; set; }

    /// <summary>Null means "see Join Instructions".</summary>
    public int? FeeCents { get; set; }

    public required string Currency { get; set; }

    /// <summary>The last clean text; the only text ever rendered publicly.</summary>
    public required string JoinInstructions { get; set; }

    /// <summary>An edit waiting for a link scan. Every clean write of <see cref="JoinInstructions"/> sets this to null.</summary>
    public string? PendingJoinInstructions { get; set; }

    public EventStatus Status { get; private set; } = EventStatus.Draft;
    public DateTimeOffset? PublishRequestedAt { get; set; }
    public HiddenReason? HiddenReason { get; private set; }
    public EventStatus? HiddenFromStatus { get; private set; }
    public EventNotice? Notice { get; set; }
    public DateTimeOffset? LastScannedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Postgres <c>xmin</c>; the ETag.</summary>
    public uint Version { get; private set; }

    public EventLifecycle Lifecycle => new(Status, HiddenReason, HiddenFromStatus);

    public bool HasEnded(DateTimeOffset now) => EndsAt <= now;

    internal void ApplyLifecycle(EventLifecycle next, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (next.Status != Status)
        {
            switch (next.Status)
            {
                case EventStatus.Published:
                    PublishedAt ??= at;
                    break;
                case EventStatus.Cancelled:
                    CancelledAt ??= at;
                    break;
                case EventStatus.Archived:
                    ArchivedAt = at;
                    break;
            }
        }

        Status = next.Status;
        HiddenReason = next.HiddenReason;
        HiddenFromStatus = next.HiddenFromStatus;
        UpdatedAt = at;
    }
}
