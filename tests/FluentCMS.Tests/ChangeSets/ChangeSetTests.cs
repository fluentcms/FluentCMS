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
            ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, Guid.NewGuid(), null, 1).Value,
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

        var result = changeSet.AddOperation(ChangeOperation.Create(OperationKind.Delete, ItemKind.Entry, item, 1, null).Value);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ChangeSetErrorCodes.ItemDuplicate, error.Code);
        Assert.Contains(item.ToString(), error.Message);
        Assert.Single(changeSet.Operations);
    }

    // A set reopened to fix an item takes the item's newer draft: the same change from the same published version, proposing the
    // later draft, so the item still has one diff and one version to restore (CHG-03, CHG-06).
    [Theory]
    [InlineData(OperationKind.Create, null, 1)]
    [InlineData(OperationKind.Update, 3, 4)]
    public void An_update_from_the_version_a_set_proposes_moves_the_item_s_operation_on(OperationKind kind, int? baseVersion, int proposed)
    {
        var changeSet = TestChangeSets.Open();
        var item = Guid.NewGuid();
        changeSet.AddOperation(ChangeOperation.Create(kind, ItemKind.Entry, item, baseVersion, proposed).Value);

        var result = changeSet.AddOperation(ChangeOperation.Create(kind, ItemKind.Entry, item, baseVersion, proposed + 1).Value);

        Assert.True(result.IsSuccess);
        var operation = Assert.Single(changeSet.Operations);
        Assert.Equal((kind, baseVersion, (int?)proposed + 1), (operation.Kind, operation.BaseVersion, operation.ProposedVersion));
    }

    [Fact]
    public void An_operation_that_proposes_no_later_draft_or_that_changes_a_content_type_is_still_a_duplicate()
    {
        var changeSet = TestChangeSets.Open();
        var entry = Guid.NewGuid();
        var type = Guid.NewGuid();
        changeSet.AddOperation(TestChangeSets.Update(ItemKind.Entry, entry));
        changeSet.AddOperation(TestChangeSets.Update(ItemKind.Schema, type));

        var stale = changeSet.AddOperation(ChangeOperation.Create(OperationKind.Update, ItemKind.Entry, entry, 1, 2).Value);
        var schema = changeSet.AddOperation(ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, type, 2, 3).Value);

        Assert.Equal(ChangeSetErrorCodes.ItemDuplicate, Assert.Single(stale.Errors).Code);
        Assert.Contains("update it from version 2", stale.Errors[0].Message);
        Assert.Equal(ChangeSetErrorCodes.ItemDuplicate, Assert.Single(schema.Errors).Code);
        Assert.Equal([2, 2], changeSet.Operations.Select(operation => operation.ProposedVersion!.Value));
    }

    // An item another set published after this one proposed it comes back as an update from that published version, which this set
    // cannot take, so the agent is told to propose the change anew rather than to update from a version it cannot.
    [Fact]
    public void An_item_published_by_another_set_since_says_so()
    {
        var changeSet = TestChangeSets.Open();
        var entry = Guid.NewGuid();
        changeSet.AddOperation(ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, entry, null, 1).Value);

        var result = changeSet.AddOperation(ChangeOperation.Create(OperationKind.Update, ItemKind.Entry, entry, 2, 3).Value);

        Assert.Equal(ChangeSetErrorCodes.ItemDuplicate, Assert.Single(result.Errors).Code);
        Assert.Contains("was published by another change set", result.Errors[0].Message);
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

    [Fact]
    public void A_set_lists_each_source_once()
    {
        var changeSet = ChangeSet.Open(Site, "Sync prices.", ["exports/catalog.csv", " exports/catalog.csv ", "Product team"], TestChangeSets.Agent).Value;

        Assert.Equal(["exports/catalog.csv", "Product team"], changeSet.Sources);
    }

    [Fact]
    public void A_set_opened_for_a_session_keeps_its_session_id()
    {
        var changeSet = ChangeSet.Open(Site, "Sync prices.", [], TestChangeSets.Agent, "example-session-1").Value;

        Assert.Equal("example-session-1", changeSet.SessionId);
        Assert.Null(TestChangeSets.Open().SessionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_session_id_is_rejected(string sessionId)
    {
        var result = ChangeSet.Open(Site, "Sync prices.", [], TestChangeSets.Agent, sessionId);

        Assert.Equal(ChangeSetErrorCodes.SessionIdInvalid, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void A_session_id_longer_than_the_limit_is_rejected()
    {
        var result = ChangeSet.Open(Site, "Sync prices.", [], TestChangeSets.Agent, new string('s', ChangeSet.MaxSessionIdLength + 1));

        Assert.Equal(ChangeSetErrorCodes.SessionIdInvalid, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Sources_added_while_open_are_appended_once()
    {
        var changeSet = TestChangeSets.Open();

        Assert.True(changeSet.AddSources([" https://example.com/spec-sheet ", "exports/catalog-2026-10-18.csv", "https://example.com/spec-sheet"]).IsSuccess);

        Assert.Equal(["exports/catalog-2026-10-18.csv", "https://example.com/spec-sheet"], changeSet.Sources);
    }

    [Fact]
    public void A_blank_source_is_not_added()
    {
        var changeSet = TestChangeSets.Open();

        var result = changeSet.AddSources(["https://example.com/spec-sheet", " "]);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ChangeSetErrorCodes.SourceBlank, error.Code);
        Assert.Contains("Source 2", error.Message);
        Assert.Equal(["exports/catalog-2026-10-18.csv"], changeSet.Sources);
    }

    [Fact]
    public void Sources_are_added_only_while_the_set_is_open()
    {
        var changeSet = TestChangeSets.InState(ChangeSetState.ChecksRunning);

        var result = changeSet.AddSources(["https://example.com/spec-sheet"]);

        Assert.Equal(ChangeSetErrorCodes.NotOpen, Assert.Single(result.Errors).Code);
        Assert.Single(changeSet.Sources);
    }

    [Fact]
    public void A_stored_set_is_restored_as_it_was()
    {
        var id = Guid.NewGuid();
        var operation = TestChangeSets.Update(ItemKind.Schema);

        var changeSet = ChangeSet.Restore(
            id,
            Site,
            "Sync prices.",
            ["exports/catalog.csv"],
            TestChangeSets.Agent,
            "example-session-1",
            [operation],
            ChangeSetState.Rejected,
            "The price has no source.");

        Assert.Equal(id, changeSet.Id);
        Assert.Equal(Site, changeSet.SiteId);
        Assert.Equal("Sync prices.", changeSet.Rationale);
        Assert.Equal(["exports/catalog.csv"], changeSet.Sources);
        Assert.Same(TestChangeSets.Agent, changeSet.Initiator);
        Assert.Equal("example-session-1", changeSet.SessionId);
        Assert.Equal([operation], changeSet.Operations);
        Assert.Equal(ChangeSetState.Rejected, changeSet.State);
        Assert.Equal("The price has no source.", changeSet.RejectionReason);
        Assert.Null(changeSet.RevertsChangeSetId);
    }

    [Fact]
    public void A_revert_holds_the_inverse_of_every_operation_in_order_and_names_the_set_it_undoes()
    {
        var create = ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, Guid.NewGuid(), null, 1).Value;
        var update = ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, Guid.NewGuid(), 1, 2).Value;
        var original = ChangeSet.Restore(
            Guid.NewGuid(),
            Site,
            "Sync prices.",
            ["exports/catalog.csv"],
            TestChangeSets.Agent,
            null,
            [create, update],
            ChangeSetState.Published,
            null);
        var person = new UserInitiator(Guid.NewGuid(), "Amir");

        var revert = original.Revert("  The sync used the wrong export. ", person).Value;

        Assert.NotEqual(original.Id, revert.Id);
        Assert.Equal(original.Id, revert.RevertsChangeSetId);
        Assert.Equal(Site, revert.SiteId);
        Assert.Equal("The sync used the wrong export.", revert.Rationale);
        Assert.Empty(revert.Sources);
        Assert.Same(person, revert.Initiator);
        Assert.Equal(ChangeSetState.Open, revert.State);
        Assert.Equal([create.Inverse(), update.Inverse()], revert.Operations);
        Assert.Equal(ChangeSetState.Published, original.State);
    }

    [Theory]
    [InlineData(ChangeSetState.Open)]
    [InlineData(ChangeSetState.Approved)]
    [InlineData(ChangeSetState.Rejected)]
    [InlineData(ChangeSetState.Reverted)]
    public void Only_a_published_set_can_be_reverted(ChangeSetState state)
    {
        var changeSet = TestChangeSets.InState(state);

        var revert = changeSet.Revert("Undo it.", TestChangeSets.Agent);

        Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(revert.Errors).Code);
    }

    [Fact]
    public void A_revert_needs_a_rationale_and_takes_no_further_operations()
    {
        var published = TestChangeSets.InState(ChangeSetState.Published);

        var blank = published.Revert(" ", TestChangeSets.Agent);
        var revert = published.Revert("Undo it.", TestChangeSets.Agent).Value;
        var added = revert.AddOperation(TestChangeSets.Update(ItemKind.Schema));

        Assert.Equal(ChangeSetErrorCodes.RationaleMissing, Assert.Single(blank.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.RevertFixed, Assert.Single(added.Errors).Code);
        Assert.Single(revert.Operations);
    }
}
