using HockeyIndex.Api.Domain;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class EventStateMachineTests
{
    private static readonly EventLifecycle[] Lifecycles =
    [
        EventLifecycle.Draft,
        new(EventStatus.Published, null, null),
        new(EventStatus.Cancelled, null, null),
        new(EventStatus.Hidden, HiddenReason.Reports, EventStatus.Published),
        new(EventStatus.Hidden, HiddenReason.Reports, EventStatus.Cancelled),
        new(EventStatus.Hidden, HiddenReason.Reports, EventStatus.Archived),
        new(EventStatus.Archived, null, null),
        new(EventStatus.Archived, HiddenReason.Reports, null),
    ];

    private static readonly HashSet<(EventStatus, EventStatus)> ReachablePairs = ComputeReachablePairs();

    public static TheoryData<EventStatus, EventStatus> AllStatusPairs()
    {
        var data = new TheoryData<EventStatus, EventStatus>();
        foreach (var from in Enum.GetValues<EventStatus>())
        {
            foreach (var to in Enum.GetValues<EventStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllStatusPairs))]
    public void Every_status_pair_is_reachable_only_when_listed_in_the_edge_table(EventStatus from, EventStatus to) =>
        Assert.Equal(EventStateMachine.Edges.Contains((from, to)), ReachablePairs.Contains((from, to)));

    [Fact]
    public void Edge_table_matches_the_plan()
    {
        HashSet<(EventStatus, EventStatus)> plan =
        [
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
        ];

        Assert.True(plan.SetEquals(EventStateMachine.Edges));
    }

    [Fact]
    public void Draft_publishes()
    {
        var next = EventStateMachine.Apply(EventLifecycle.Draft, EventTrigger.Publish, hasEnded: false);

        Assert.Equal(new EventLifecycle(EventStatus.Published, null, null), next);
    }

    [Fact]
    public void Published_cancels()
    {
        var next = EventStateMachine.Apply(Published, EventTrigger.Cancel, hasEnded: false);

        Assert.Equal(new EventLifecycle(EventStatus.Cancelled, null, null), next);
    }

    [Theory]
    [InlineData(EventStatus.Published)]
    [InlineData(EventStatus.Cancelled)]
    [InlineData(EventStatus.Archived)]
    public void Hide_records_reason_and_previous_status(EventStatus from)
    {
        var next = EventStateMachine.Apply(new EventLifecycle(from, null, null), EventTrigger.Hide, hasEnded: false, HiddenReason.LinkScan);

        Assert.Equal(new EventLifecycle(EventStatus.Hidden, HiddenReason.LinkScan, from), next);
    }

    [Fact]
    public void Hide_requires_a_reason() =>
        Assert.Throws<ArgumentNullException>(() => EventStateMachine.Apply(Published, EventTrigger.Hide, hasEnded: false));

    [Theory]
    [InlineData(EventStatus.Published)]
    [InlineData(EventStatus.Cancelled)]
    public void Restore_before_end_returns_to_the_previous_status(EventStatus previous)
    {
        var next = EventStateMachine.Apply(new EventLifecycle(EventStatus.Hidden, HiddenReason.Admin, previous), EventTrigger.Restore, hasEnded: false);

        Assert.Equal(new EventLifecycle(previous, null, null), next);
    }

    [Fact]
    public void Restore_after_end_archives_and_clears_the_reason()
    {
        var next = EventStateMachine.Apply(
            new EventLifecycle(EventStatus.Hidden, HiddenReason.Reports, EventStatus.Published), EventTrigger.Restore, hasEnded: true);

        Assert.Equal(new EventLifecycle(EventStatus.Archived, null, null), next);
    }

    [Fact]
    public void Restore_of_a_hidden_archived_event_archives_it_again()
    {
        var next = EventStateMachine.Apply(
            new EventLifecycle(EventStatus.Hidden, HiddenReason.Reports, EventStatus.Archived), EventTrigger.Restore, hasEnded: false);

        Assert.Equal(new EventLifecycle(EventStatus.Archived, null, null), next);
    }

    [Theory]
    [InlineData(EventStatus.Published)]
    [InlineData(EventStatus.Cancelled)]
    public void Sweep_archives_ended_public_events(EventStatus from)
    {
        var next = EventStateMachine.Apply(new EventLifecycle(from, null, null), EventTrigger.Sweep, hasEnded: true);

        Assert.Equal(new EventLifecycle(EventStatus.Archived, null, null), next);
    }

    [Fact]
    public void Sweep_of_a_hidden_event_keeps_the_reason()
    {
        var next = EventStateMachine.Apply(
            new EventLifecycle(EventStatus.Hidden, HiddenReason.Reports, EventStatus.Published), EventTrigger.Sweep, hasEnded: true);

        Assert.Equal(new EventLifecycle(EventStatus.Archived, HiddenReason.Reports, null), next);
    }

    [Fact]
    public void Sweep_before_end_throws() =>
        Assert.Throws<InvalidEventTransitionException>(() => EventStateMachine.Apply(Published, EventTrigger.Sweep, hasEnded: false));

    [Fact]
    public void Clearing_the_reason_keeps_the_event_archived()
    {
        var next = EventStateMachine.Apply(
            new EventLifecycle(EventStatus.Archived, HiddenReason.Reports, null), EventTrigger.ClearHiddenReason, hasEnded: true);

        Assert.Equal(new EventLifecycle(EventStatus.Archived, null, null), next);
    }

    [Fact]
    public void Clearing_a_missing_reason_throws() =>
        Assert.Throws<InvalidEventTransitionException>(() =>
            EventStateMachine.Apply(new EventLifecycle(EventStatus.Archived, null, null), EventTrigger.ClearHiddenReason, hasEnded: true));

    [Theory]
    [InlineData(EventStatus.Published, EventTrigger.Publish)]
    [InlineData(EventStatus.Draft, EventTrigger.Cancel)]
    [InlineData(EventStatus.Cancelled, EventTrigger.Cancel)]
    [InlineData(EventStatus.Draft, EventTrigger.Hide)]
    [InlineData(EventStatus.Published, EventTrigger.Restore)]
    [InlineData(EventStatus.Draft, EventTrigger.Sweep)]
    [InlineData(EventStatus.Archived, EventTrigger.Sweep)]
    public void Forbidden_triggers_throw(EventStatus from, EventTrigger trigger)
    {
        var exception = Assert.Throws<InvalidEventTransitionException>(() =>
            EventStateMachine.Apply(new EventLifecycle(from, null, null), trigger, hasEnded: true, HiddenReason.Admin));

        Assert.Equal(trigger, exception.Trigger);
    }

    private static EventLifecycle Published => new(EventStatus.Published, null, null);

    private static HashSet<(EventStatus, EventStatus)> ComputeReachablePairs()
    {
        var pairs = new HashSet<(EventStatus, EventStatus)>();
        foreach (var lifecycle in Lifecycles)
        {
            foreach (var trigger in Enum.GetValues<EventTrigger>())
            {
                foreach (var hasEnded in new[] { false, true })
                {
                    try
                    {
                        var next = EventStateMachine.Apply(lifecycle, trigger, hasEnded, HiddenReason.Admin);
                        pairs.Add((lifecycle.Status, next.Status));
                    }
                    catch (InvalidEventTransitionException)
                    {
                    }
                }
            }
        }

        return pairs;
    }
}
