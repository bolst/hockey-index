namespace HockeyIndex.Api.Domain;

public enum EventTrigger
{
    Publish,
    Cancel,
    Hide,
    Restore,
    Sweep,
    ClearHiddenReason,
}

/// <summary>The status columns that only <c>EventTransitions</c> writes.</summary>
public sealed record EventLifecycle(EventStatus Status, HiddenReason? HiddenReason, EventStatus? HiddenFromStatus)
{
    public static EventLifecycle Draft { get; } = new(EventStatus.Draft, null, null);
}

public sealed class InvalidEventTransitionException(EventLifecycle from, EventTrigger trigger)
    : InvalidOperationException($"Event in {from.Status} (hidden reason {from.HiddenReason?.ToString() ?? "none"}) cannot {trigger}.")
{
    public EventLifecycle From { get; } = from;
    public EventTrigger Trigger { get; } = trigger;
}

/// <summary>Pure M3 edge table (AC-LC-2). Every pair not listed in <see cref="Edges"/> throws.</summary>
public static class EventStateMachine
{
    public static IReadOnlySet<(EventStatus From, EventStatus To)> Edges { get; } = new HashSet<(EventStatus, EventStatus)>
    {
        (EventStatus.Draft, EventStatus.Published),
        (EventStatus.Published, EventStatus.Cancelled),
        (EventStatus.Published, EventStatus.Hidden),
        (EventStatus.Cancelled, EventStatus.Hidden),
        (EventStatus.Archived, EventStatus.Hidden),
        (EventStatus.Hidden, EventStatus.Published),
        (EventStatus.Hidden, EventStatus.Cancelled),
        (EventStatus.Hidden, EventStatus.Archived),
        (EventStatus.Published, EventStatus.Archived),
        (EventStatus.Cancelled, EventStatus.Archived),
        (EventStatus.Archived, EventStatus.Archived),
    };

    /// <param name="hasEnded"><c>ends_at &lt;= now</c>.</param>
    /// <param name="hideReason">Required for <see cref="EventTrigger.Hide"/>; ignored otherwise.</param>
    public static EventLifecycle Apply(EventLifecycle current, EventTrigger trigger, bool hasEnded, HiddenReason? hideReason = null)
    {
        ArgumentNullException.ThrowIfNull(current);

        return (trigger, current.Status) switch
        {
            (EventTrigger.Publish, EventStatus.Draft) => new(EventStatus.Published, null, null),
            (EventTrigger.Cancel, EventStatus.Published) => new(EventStatus.Cancelled, null, null),
            (EventTrigger.Hide, EventStatus.Published or EventStatus.Cancelled or EventStatus.Archived) =>
                new(EventStatus.Hidden, hideReason ?? throw new ArgumentNullException(nameof(hideReason)), current.Status),
            (EventTrigger.Restore, EventStatus.Hidden) => Restore(current, hasEnded),
            (EventTrigger.Sweep, EventStatus.Published or EventStatus.Cancelled) when hasEnded => new(EventStatus.Archived, null, null),
            (EventTrigger.Sweep, EventStatus.Hidden) when hasEnded => new(EventStatus.Archived, current.HiddenReason, null),
            (EventTrigger.ClearHiddenReason, EventStatus.Archived) when current.HiddenReason is not null => new(EventStatus.Archived, null, null),
            _ => throw new InvalidEventTransitionException(current, trigger),
        };
    }

    private static EventLifecycle Restore(EventLifecycle current, bool hasEnded) =>
        !hasEnded && current.HiddenFromStatus is EventStatus.Published or EventStatus.Cancelled
            ? new(current.HiddenFromStatus.Value, null, null)
            : new(EventStatus.Archived, null, null);
}
