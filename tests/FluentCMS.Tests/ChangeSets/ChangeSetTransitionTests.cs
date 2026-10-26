using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;

namespace FluentCMS.Tests.ChangeSets;

public sealed class ChangeSetTransitionTests
{
    public static TheoryData<ChangeSetState, ChangeSetState> Allowed()
    {
        var data = new TheoryData<ChangeSetState, ChangeSetState>();
        foreach (var (from, to) in ChangeSetTransitions.All)
        {
            data.Add(from, to);
        }

        return data;
    }

    public static TheoryData<ChangeSetState, ChangeSetState> NotAllowed()
    {
        var data = new TheoryData<ChangeSetState, ChangeSetState>();
        foreach (var from in Enum.GetValues<ChangeSetState>())
        {
            foreach (var to in Enum.GetValues<ChangeSetState>().Where(to => !ChangeSetTransitions.IsAllowed(from, to)))
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Fact]
    public void The_table_holds_exactly_the_chg_04_transitions()
    {
        Assert.Equal(
            [
                (ChangeSetState.Open, ChangeSetState.ChecksRunning),
                (ChangeSetState.ChecksRunning, ChangeSetState.ChecksFailed),
                (ChangeSetState.ChecksRunning, ChangeSetState.AwaitingReview),
                (ChangeSetState.ChecksRunning, ChangeSetState.Approved),
                (ChangeSetState.ChecksFailed, ChangeSetState.Open),
                (ChangeSetState.ChecksFailed, ChangeSetState.Rejected),
                (ChangeSetState.AwaitingReview, ChangeSetState.Approved),
                (ChangeSetState.AwaitingReview, ChangeSetState.Rejected),
                (ChangeSetState.Approved, ChangeSetState.Published),
                (ChangeSetState.Approved, ChangeSetState.Rejected),
                (ChangeSetState.Published, ChangeSetState.Reverted),
            ],
            ChangeSetTransitions.All);
    }

    [Fact]
    public void Every_state_can_be_reached_from_open()
    {
        foreach (var state in Enum.GetValues<ChangeSetState>())
        {
            Assert.Equal(state, TestChangeSets.InState(state).State);
        }
    }

    [Theory]
    [InlineData(ChangeSetState.Rejected)]
    [InlineData(ChangeSetState.Reverted)]
    public void Rejected_and_reverted_are_final(ChangeSetState state)
    {
        Assert.Empty(ChangeSetTransitions.From(state));
    }

    [Fact]
    public void Publish_has_one_way_in_from_approved()
    {
        var ways = ChangeSetTransitions.All.Where(transition => transition.To == ChangeSetState.Published);

        Assert.Equal([ChangeSetState.Approved], ways.Select(transition => transition.From));
    }

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Every_transition_in_the_table_succeeds(ChangeSetState from, ChangeSetState to)
    {
        var changeSet = TestChangeSets.InState(from);

        // A set in review is approved by its approvals (RTE-05).
        var result = (from, to) switch
        {
            (_, ChangeSetState.Rejected) => changeSet.Reject("The price has no source."),
            (ChangeSetState.AwaitingReview, ChangeSetState.Approved) => changeSet.Approve(new UserInitiator(Guid.NewGuid(), "Amir"), DateTimeOffset.UnixEpoch),
            _ => changeSet.TransitionTo(to),
        };

        Assert.True(result.IsSuccess);
        Assert.Equal(to, changeSet.State);
    }

    [Theory]
    [MemberData(nameof(NotAllowed))]
    public void Every_transition_outside_the_table_is_refused_and_changes_nothing(ChangeSetState from, ChangeSetState to)
    {
        var changeSet = TestChangeSets.InState(from);
        var reason = changeSet.RejectionReason;

        var result = changeSet.TransitionTo(to);

        Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(result.Errors).Code);
        Assert.Equal(from, changeSet.State);
        Assert.Equal(reason, changeSet.RejectionReason);
    }

    [Theory]
    [InlineData(ChangeSetState.Open)]
    [InlineData(ChangeSetState.ChecksRunning)]
    [InlineData(ChangeSetState.Published)]
    [InlineData(ChangeSetState.Rejected)]
    [InlineData(ChangeSetState.Reverted)]
    public void A_rejection_with_a_reason_is_still_refused_where_the_table_has_none(ChangeSetState from)
    {
        var changeSet = TestChangeSets.InState(from);
        var reason = changeSet.RejectionReason;

        var result = changeSet.Reject("The price has no source.");

        Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(result.Errors).Code);
        Assert.Equal(from, changeSet.State);
        Assert.Equal(reason, changeSet.RejectionReason);
    }

    [Fact]
    public void A_refusal_names_the_states_the_set_can_move_to()
    {
        var running = TestChangeSets.InState(ChangeSetState.ChecksRunning);
        var rejected = TestChangeSets.InState(ChangeSetState.Rejected);

        var fromRunning = Assert.Single(running.TransitionTo(ChangeSetState.Published).Errors).Message;
        var fromRejected = Assert.Single(rejected.TransitionTo(ChangeSetState.Open).Errors).Message;

        Assert.Equal(
            "A change set cannot move from checks running to published; it can move to checks failed or awaiting review or approved.",
            fromRunning);
        Assert.Equal("A change set cannot move from rejected to open; rejected is final.", fromRejected);
    }

    [Theory]
    [InlineData(ChangeSetState.ChecksFailed)]
    [InlineData(ChangeSetState.AwaitingReview)]
    [InlineData(ChangeSetState.Approved)]
    public void A_rejection_without_a_reason_is_refused(ChangeSetState from)
    {
        var changeSet = TestChangeSets.InState(from);

        var errors = new[] { changeSet.TransitionTo(ChangeSetState.Rejected), changeSet.Reject("   "), changeSet.Reject(null!) }
            .Select(result => Assert.Single(result.Errors).Code);

        Assert.All(errors, code => Assert.Equal(ChangeSetErrorCodes.RejectionReasonMissing, code));
        Assert.Equal(from, changeSet.State);
        Assert.Null(changeSet.RejectionReason);
    }

    [Fact]
    public void A_rejection_stores_its_reason()
    {
        var changeSet = TestChangeSets.InState(ChangeSetState.AwaitingReview);

        Assert.True(changeSet.Reject("  The price has no source. ").IsSuccess);

        Assert.Equal(ChangeSetState.Rejected, changeSet.State);
        Assert.Equal("The price has no source.", changeSet.RejectionReason);
    }
}
