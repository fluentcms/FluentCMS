using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static FluentCMS.IntegrationTests.ChangeSets.EntryChangeSetTests;

namespace FluentCMS.IntegrationTests.ChangeSets;

public sealed class EntryStateTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_new_entry_is_a_draft_in_review_while_its_set_is_and_published_with_it(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, _) = await CatalogAsync(scenario);
        var changeSet = await scenario.OpenAsync(Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));

        Assert.Equal(EntryState.Draft, await StateAsync(scenario, page));

        ChangeSetState[] path = [ChangeSetState.ChecksRunning, ChangeSetState.ChecksFailed];
        foreach (var target in path)
        {
            await MoveAsync(scenario, changeSet.Id, target);
            Assert.Equal(EntryState.InReview, await StateAsync(scenario, page));
        }

        await MoveAsync(scenario, changeSet.Id, ChangeSetState.Open);
        Assert.Equal(EntryState.Draft, await StateAsync(scenario, page));

        foreach (var target in new[] { ChangeSetState.ChecksRunning, ChangeSetState.AwaitingReview, ChangeSetState.Approved })
        {
            await MoveAsync(scenario, changeSet.Id, target);
            Assert.Equal(EntryState.InReview, await StateAsync(scenario, page));
        }

        Assert.True((await scenario.RunAsync(directory => directory.PublishAsync(changeSet.Id))).IsSuccess);
        Assert.Equal(EntryState.Published, await StateAsync(scenario, page));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_entry_stays_in_review_until_no_set_reviews_it_and_then_shows_its_earlier_state(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, _) = await CatalogAsync(scenario);
        await scenario.PublishAsync(Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));
        await UpdatePageAsync(scenario, page, 1, "Widget Pro");
        var first = await scenario.OpenAsync(Operation(OperationKind.Update, ItemKind.Entry, page, 1, 2));
        var second = await scenario.OpenAsync(Operation(OperationKind.Delete, ItemKind.Entry, page, 1, null));
        foreach (var changeSet in new[] { first, second })
        {
            await MoveAsync(scenario, changeSet.Id, ChangeSetState.ChecksRunning);
            await MoveAsync(scenario, changeSet.Id, ChangeSetState.AwaitingReview);
        }

        Assert.True((await scenario.RunAsync(directory => directory.RejectAsync(first.Id, "Not this title."))).IsSuccess);
        Assert.Equal(EntryState.InReview, await StateAsync(scenario, page));

        Assert.True((await scenario.RunAsync(directory => directory.RejectAsync(second.Id, "Keep the page."))).IsSuccess);
        Assert.Equal(EntryState.Published, await StateAsync(scenario, page));
        Assert.Equal(
            [new EntrySummary(page, 2, 1, EntryState.Published)],
            (await EntriesAsync(scenario, entries => entries.ListAsync("page"))).Value);
    }

    // Two reverts of one set can both be approved. Once one publishes the other is stale, and a person's rejection clears it,
    // which takes its entries out of review.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_stale_approved_revert_is_rejected_with_a_reason_and_its_entries_leave_review(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, _) = await CatalogAsync(scenario);
        var published = await scenario.PublishAsync(Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));
        var first = (await scenario.RunAsync(directory => directory.RevertAsync(published.Id, "The page is not ready."))).Value;
        var second = (await scenario.RunAsync(directory => directory.RevertAsync(published.Id, "The page is not ready."))).Value;
        foreach (var revert in new[] { first, second })
        {
            await MoveAsync(scenario, revert.Id, ChangeSetState.ChecksRunning);
            await MoveAsync(scenario, revert.Id, ChangeSetState.Approved);
        }

        Assert.True((await scenario.RunAsync(directory => directory.PublishAsync(first.Id))).IsSuccess);
        var stale = await scenario.RunAsync(directory => directory.PublishAsync(second.Id));
        Assert.Equal(ChangeSetErrorCodes.BaseVersionStale, Assert.Single(stale.Errors).Code);
        Assert.Equal(EntryState.InReview, await StateAsync(scenario, page));

        var blank = await scenario.RunAsync(directory => directory.RejectAsync(second.Id, " "));
        var rejected = await scenario.RunAsync(directory => directory.RejectAsync(second.Id, "The first revert already took the page down."));

        Assert.Equal(ChangeSetErrorCodes.RejectionReasonMissing, Assert.Single(blank.Errors).Code);
        Assert.Equal(ChangeSetState.Rejected, rejected.Value.State);
        Assert.Equal(EntryState.Reverted, await StateAsync(scenario, page));
        Assert.Equal(ChangeSetState.Reverted, (await scenario.ChangeSetRowAsync(published.Id)).State);
        Assert.Equal(1, await scenario.QueryAsync(context => context.AuditEvents.CountAsync(row => row.Operation == "change_set.reverted")));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Publishing_deletes_reverts_and_updates_archives_reverts_and_publishes_the_entry(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, _) = await CatalogAsync(scenario);
        await scenario.PublishAsync(Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));

        var delete = await scenario.PublishAsync(Operation(OperationKind.Delete, ItemKind.Entry, page, 1, null));
        Assert.Equal(EntryState.Archived, await StateAsync(scenario, page));

        var undoDelete = (await scenario.RunAsync(directory => directory.RevertAsync(delete.Id, "The page is still needed."))).Value;
        await scenario.PublishAsync(undoDelete.Id);
        Assert.Equal(EntryState.Reverted, await StateAsync(scenario, page));

        await UpdatePageAsync(scenario, page, 1, "Widget Pro");
        var update = await scenario.PublishAsync(Operation(OperationKind.Update, ItemKind.Entry, page, 1, 2));
        Assert.Equal(EntryState.Published, await StateAsync(scenario, page));

        var undoUpdate = (await scenario.RunAsync(directory => directory.RevertAsync(update.Id, "The new title was wrong."))).Value;
        await scenario.PublishAsync(undoUpdate.Id);
        Assert.Equal(EntryState.Reverted, await StateAsync(scenario, page));
        Assert.Equal(1, await PublishedVersionOfAsync(scenario, page));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Writes_deletes_and_restores_leave_the_state_as_it_is(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, _) = await CatalogAsync(scenario);
        await scenario.PublishAsync(Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));

        await UpdatePageAsync(scenario, page, 1, "Widget Pro");
        Assert.Equal(EntryState.Published, await StateAsync(scenario, page));

        await EntriesAsync(scenario, entries => entries.DeleteAsync(page, 2));
        var restored = (await EntriesAsync(scenario, entries => entries.RestoreAsync(page))).Value;

        Assert.Equal(EntryState.Published, restored.State);
        Assert.Equal(EntryState.Published, await StateAsync(scenario, page));
    }

    private static async Task MoveAsync(ChangeSetScenario scenario, Guid changeSetId, ChangeSetState target)
    {
        var moved = await scenario.RunAsync(directory => directory.TransitionAsync(changeSetId, target));
        Assert.True(moved.IsSuccess, string.Join("; ", moved.Errors.Select(error => error.Message)));
    }

    private static async Task<EntryState> StateAsync(ChangeSetScenario scenario, Guid entry)
    {
        var read = (await EntriesAsync(scenario, entries => entries.GetAsync(entry))).Value.State;
        var listed = Assert.Single((await EntriesAsync(scenario, entries => entries.ListAsync("page"))).Value, summary => summary.Id == entry).State;

        Assert.Equal(read, listed);
        return read;
    }
}
