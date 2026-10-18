using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;

namespace FluentCMS.Tests.ChangeSets;

public sealed class ChangeSetTests
{
    private static readonly Guid Site = TestChangeSets.Site;

    [Fact]
    public void A_new_set_is_open_and_keeps_its_site_rationale_sources_and_initiator()
    {
        var user = new UserInitiator(Guid.NewGuid(), "Example Editor");

        var changeSet = ChangeSet.Open(Site, "  Correct the launch date. ", [" https://example.com/press-release ", "Product team"], user).Value;

        Assert.NotEqual(Guid.Empty, changeSet.Id);
        Assert.Equal(Site, changeSet.SiteId);
        Assert.Equal("Correct the launch date.", changeSet.Rationale);
        Assert.Equal(["https://example.com/press-release", "Product team"], changeSet.Sources);
        Assert.Same(user, changeSet.Initiator);
        Assert.Equal(ChangeSetState.Open, changeSet.State);
        Assert.Empty(changeSet.Operations);
        Assert.Null(changeSet.RejectionReason);
    }

    [Fact]
    public void An_agent_stays_the_initiator_of_the_sets_it_opens()
    {
        var changeSet = ChangeSet.Open(Site, "Sync prices.", [], TestChangeSets.Agent).Value;

        Assert.IsType<AgentInitiator>(changeSet.Initiator);
    }

    [Fact]
    public void A_set_without_sources_opens()
    {
        var result = ChangeSet.Open(Site, "Fix a typo.", [], TestChangeSets.Agent);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Sources);
    }

    [Fact]
    public void Every_problem_with_a_new_set_is_reported_at_once()
    {
        var result = ChangeSet.Open(Guid.Empty, "   ", ["exports/catalog.csv", " ", ""], TestChangeSets.Agent);

        Assert.Equal(
            [ChangeSetErrorCodes.SiteMissing, ChangeSetErrorCodes.RationaleMissing, ChangeSetErrorCodes.SourceBlank, ChangeSetErrorCodes.SourceBlank],
            result.Errors.Select(error => error.Code));
        Assert.Contains("Source 2", result.Errors[2].Message);
        Assert.Contains("Source 3", result.Errors[3].Message);
    }

    [Fact]
    public void A_set_needs_an_initiator()
    {
        Assert.Throws<ArgumentNullException>(() => ChangeSet.Open(Site, "Sync prices.", [], null!));
    }

    [Fact]
    public void Entity_entry_and_schema_operations_can_share_a_set()
    {
        var changeSet = TestChangeSets.Open();
        var operations = new[]
        {
            TestChangeSets.Update(ItemKind.Entity),
            TestChangeSets.Update(ItemKind.Entry),
            ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, Guid.NewGuid(), null).Value,
        };

        foreach (var operation in operations)
        {
            Assert.True(changeSet.AddOperation(operation).IsSuccess);
        }

        Assert.True(changeSet.TransitionTo(ChangeSetState.ChecksRunning).IsSuccess);
        Assert.Equal(operations, changeSet.Operations);
    }

    [Fact]
    public void An_item_appears_at_most_once_in_a_set()
    {
        var changeSet = TestChangeSets.Open();
        var item = Guid.NewGuid();
        changeSet.AddOperation(TestChangeSets.Update(ItemKind.Entry, item));

        var result = changeSet.AddOperation(ChangeOperation.Create(OperationKind.Delete, ItemKind.Entry, item, 1).Value);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ChangeSetErrorCodes.ItemDuplicate, error.Code);
        Assert.Contains(item.ToString(), error.Message);
        Assert.Single(changeSet.Operations);
    }

    [Fact]
    public void A_set_with_no_operations_cannot_leave_open()
    {
        var changeSet = TestChangeSets.Open();

        var result = changeSet.TransitionTo(ChangeSetState.ChecksRunning);

        Assert.Equal(ChangeSetErrorCodes.OperationsMissing, Assert.Single(result.Errors).Code);
        Assert.Equal(ChangeSetState.Open, changeSet.State);
    }

    [Theory]
    [InlineData(ChangeSetState.ChecksRunning)]
    [InlineData(ChangeSetState.AwaitingReview)]
    [InlineData(ChangeSetState.Published)]
    [InlineData(ChangeSetState.Rejected)]
    public void Operations_are_added_only_while_the_set_is_open(ChangeSetState state)
    {
        var changeSet = TestChangeSets.InState(state);

        var result = changeSet.AddOperation(TestChangeSets.Update(ItemKind.Media));

        Assert.Equal(ChangeSetErrorCodes.NotOpen, Assert.Single(result.Errors).Code);
        Assert.Single(changeSet.Operations);
    }

    [Fact]
    public void A_set_whose_checks_failed_takes_operations_again_once_reopened()
    {
        var changeSet = TestChangeSets.InState(ChangeSetState.ChecksFailed);

        Assert.True(changeSet.TransitionTo(ChangeSetState.Open).IsSuccess);
        Assert.True(changeSet.AddOperation(TestChangeSets.Update(ItemKind.Entity)).IsSuccess);
        Assert.Equal(2, changeSet.Operations.Count);
    }
}
