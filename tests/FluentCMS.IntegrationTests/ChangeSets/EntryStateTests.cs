using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Infrastructure.Persistence;
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
