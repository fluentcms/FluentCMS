using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.ChangeSets;

public sealed class ChangeSetRevertTests
{
    private const string WrongExport = "The sync used the wrong export.";

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Publishing_a_revert_restores_every_item_and_moves_the_original_to_reverted(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (category, product, region, original) = await PublishedOriginalAsync(scenario);
        var versionRows = await VersionRowsAsync(scenario);

        var opened = await scenario.RunAsync(directory => directory.RevertAsync(original.Id, WrongExport));

        Assert.True(opened.IsSuccess, string.Join("; ", opened.Errors.Select(error => error.Message)));
        var revert = (await scenario.RunAsync(directory => directory.GetAsync(opened.Value.Id))).Value;
        Assert.Equal(original.Id, revert.RevertsChangeSetId);
        Assert.Equal(WrongExport, revert.Rationale);
        Assert.Equal(Amir, revert.Initiator);
        Assert.Equal(ChangeSetState.Open, revert.State);
        Assert.Equal(original.Operations.Select(operation => operation.Inverse()), revert.Operations);
        Assert.Equal(
            [Delete(category, 1), ChangeOperation.Restore(OperationKind.Update, ItemKind.Schema, product.Id, 2, 1), ChangeOperation.Restore(OperationKind.Create, ItemKind.Schema, region.Id, null, 1)],
            revert.Operations);

        var added = (await scenario.ChangeSetEventsAsync(revert.Id)).Where(row => row.Operation == "change_set.operation_added").ToList();
        Assert.Equal([category.Id, product.Id, region.Id], added.Select(row => row.SubjectId));
        Assert.Equal([versionRows[(category.Id, 1)], versionRows[(product.Id, 2)], null], added.Select(row => row.PreviousVersionId));

        var diff = (await scenario.RunAsync(directory => directory.DiffAsync(revert.Id))).Value;
        var summary = Assert.Single(diff.Items[1].Fields);
        Assert.Equal("summary", summary.Name);
        Assert.Equal(ValueChange.Removed, summary.Change);

        var before = (await scenario.ChangeSetEventsAsync()).Count;
        await scenario.ApproveAsync(revert.Id);
        var published = await scenario.RunAsync(directory => directory.PublishAsync(revert.Id));

        Assert.True(published.IsSuccess, string.Join("; ", published.Errors.Select(error => error.Message)));
        Assert.Null(await scenario.PublishedVersionAsync(category.Id));
        Assert.Equal(1, await scenario.PublishedVersionAsync(product.Id));
        Assert.Equal(1, await scenario.PublishedVersionAsync(region.Id));
        Assert.Equal(ChangeSetState.Published, (await scenario.ChangeSetRowAsync(revert.Id)).State);
        Assert.Equal(ChangeSetState.Reverted, (await scenario.ChangeSetRowAsync(original.Id)).State);
        Assert.Equal(ChangeSetState.Reverted, (await scenario.RunAsync(directory => directory.GetAsync(original.Id))).Value.State);

        var events = (await scenario.ChangeSetEventsAsync()).Skip(before + 2).ToList();
        Assert.Equal(
            ["content_type.unpublished", "content_type.published", "content_type.published", "change_set.published", "change_set.reverted"],
            events.Select(row => row.Operation));
        Assert.Equal([category.Id, product.Id, region.Id, revert.Id, original.Id], events.Select(row => row.SubjectId));
        Assert.Equal([revert.Id, revert.Id, revert.Id, revert.Id, original.Id], events.Select(row => row.ChangeSetId));
        Assert.Equal([versionRows[(category.Id, 1)], versionRows[(product.Id, 2)], null, null, null], events.Select(row => row.PreviousVersionId));
        Assert.All(events, row => Assert.Equal(Amir.UserId, row.InitiatorId));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Reverting_a_revert_publishes_the_original_versions_again(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (category, product, region, original) = await PublishedOriginalAsync(scenario);
        var revert = (await scenario.RunAsync(directory => directory.RevertAsync(original.Id, WrongExport))).Value;
        await scenario.PublishAsync(revert.Id);

        var again = (await scenario.RunAsync(directory => directory.RevertAsync(revert.Id, "The export was right after all."))).Value;
        await scenario.PublishAsync(again.Id);

        Assert.Equal(original.Operations, again.Operations);
        Assert.Equal(1, await scenario.PublishedVersionAsync(category.Id));
        Assert.Equal(2, await scenario.PublishedVersionAsync(product.Id));
        Assert.Null(await scenario.PublishedVersionAsync(region.Id));
        Assert.Equal(ChangeSetState.Reverted, (await scenario.ChangeSetRowAsync(revert.Id)).State);
        Assert.Equal(ChangeSetState.Reverted, (await scenario.ChangeSetRowAsync(original.Id)).State);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_set_that_is_not_published_or_whose_items_moved_on_is_not_reverted(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var category = await scenario.ContentTypeAsync("category");
        var open = await scenario.OpenAsync(Create(category));
        var original = await scenario.PublishAsync(Create(product));
        await scenario.PublishAsync(Update(product));
        var latest = await scenario.PublishAsync(Create(category));
        var sets = (await scenario.ChangeSetRowsAsync()).Count;
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        var unknown = await scenario.RunAsync(directory => directory.RevertAsync(Guid.NewGuid(), WrongExport));
        var notPublished = await scenario.RunAsync(directory => directory.RevertAsync(open.Id, WrongExport));
        var movedOn = await scenario.RunAsync(directory => directory.RevertAsync(original.Id, WrongExport));
        var withoutRationale = await scenario.RunAsync(directory => directory.RevertAsync(latest.Id, " "));
        var moved = await scenario.RunAsync(directory => directory.TransitionAsync(latest.Id, ChangeSetState.Reverted));

        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Assert.Single(unknown.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(notPublished.Errors).Code);
        Assert.Equal(
            "The content type 'product' is published at version 2, but this change set was based on version 1.",
            Assert.Single(movedOn.Errors, error => error.Code == ChangeSetErrorCodes.BaseVersionStale).Message);
        Assert.Equal(ChangeSetErrorCodes.RationaleMissing, Assert.Single(withoutRationale.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(moved.Errors).Code);
        Assert.Equal(ChangeSetState.Published, (await scenario.ChangeSetRowAsync(latest.Id)).State);
        Assert.Equal(sets, (await scenario.ChangeSetRowsAsync()).Count);
        Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);

        var revert = (await scenario.RunAsync(directory => directory.RevertAsync(latest.Id, WrongExport))).Value;
        var extra = await scenario.RunAsync(directory => directory.AddOperationAsync(revert.Id, Create(product)));
        Assert.Equal(ChangeSetErrorCodes.RevertFixed, Assert.Single(extra.Errors).Code);
        Assert.Single(await scenario.OperationRowsAsync(revert.Id));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_two_reverts_of_one_set_only_the_first_to_publish_goes_through(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (_, product, _, original) = await PublishedOriginalAsync(scenario);
        var first = (await scenario.RunAsync(directory => directory.RevertAsync(original.Id, WrongExport))).Value;
        var second = (await scenario.RunAsync(directory => directory.RevertAsync(original.Id, WrongExport))).Value;
        await scenario.ApproveAsync(second.Id);
        await scenario.PublishAsync(first.Id);
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        var refused = await scenario.RunAsync(directory => directory.PublishAsync(second.Id));

        Assert.NotEmpty(refused.Errors);
        Assert.Equal(1, await scenario.PublishedVersionAsync(product.Id));
        Assert.Equal(ChangeSetState.Approved, (await scenario.ChangeSetRowAsync(second.Id)).State);
        Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);
        Assert.Single(await scenario.ChangeSetEventsAsync(original.Id), row => row.Operation == "change_set.reverted");
    }

    // Publishing the revert appends one event per item, the revert's and the original's, each after the first item moved.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failure_mid_revert_leaves_the_items_the_revert_and_the_original_as_they_were(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (category, product, region, original) = await PublishedOriginalAsync(scenario);
        var revert = (await scenario.RunAsync(directory => directory.RevertAsync(original.Id, WrongExport))).Value;
        await scenario.ApproveAsync(revert.Id);
        var revisions = (Revert: (await scenario.ChangeSetRowAsync(revert.Id)).Revision, Original: (await scenario.ChangeSetRowAsync(original.Id)).Revision);
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        for (var append = 1; append <= 5; append++)
        {
            scenario.FailOnAppend(append);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.RunAsync(directory => directory.PublishAsync(revert.Id)));

            Assert.Equal(1, await scenario.PublishedVersionAsync(category.Id));
            Assert.Equal(2, await scenario.PublishedVersionAsync(product.Id));
            Assert.Null(await scenario.PublishedVersionAsync(region.Id));
            var storedRevert = await scenario.ChangeSetRowAsync(revert.Id);
            var storedOriginal = await scenario.ChangeSetRowAsync(original.Id);
            Assert.Equal((ChangeSetState.Approved, revisions.Revert), (storedRevert.State, storedRevert.Revision));
            Assert.Equal((ChangeSetState.Published, revisions.Original), (storedOriginal.State, storedOriginal.Revision));
            Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);
        }

        Assert.True((await scenario.RunAsync(directory => directory.PublishAsync(revert.Id))).IsSuccess);
        Assert.Equal(ChangeSetState.Reverted, (await scenario.ChangeSetRowAsync(original.Id)).State);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_cannot_revert_and_the_refusal_is_logged(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var product = await scenario.ContentTypeAsync("product");
        var original = await scenario.PublishAsync(Create(product));
        var sets = (await scenario.ChangeSetRowsAsync()).Count;

        var refused = await scenario.RunAsync(directory => directory.RevertAsync(original.Id, WrongExport));

        Assert.Equal(AgentPermissionErrorCodes.TransitionNotPermitted, Assert.Single(refused.Errors).Code);
        Assert.Equal(sets, (await scenario.ChangeSetRowsAsync()).Count);
        Assert.Equal(ChangeSetState.Published, (await scenario.ChangeSetRowAsync(original.Id)).State);
        Assert.Equal("agent.transition_refused", (await scenario.ChangeSetEventsAsync(original.Id))[^1].Operation);
    }

    // Reverting and publishing are review moves, which only a person makes (MCP-06).
    private static async Task<ChangeSetScenario> ScenarioAsync(string provider)
    {
        var scenario = await ChangeSetScenario.CreateAsync(provider);
        scenario.Initiator = Amir;
        return scenario;
    }

    // A published original with a create of category, an update of product from 1 to 2 and a delete of region at 1.
    private static async Task<(ContentType Category, ContentType Product, ContentType Region, ChangeSet Original)> PublishedOriginalAsync(
        ChangeSetScenario scenario)
    {
        var category = await scenario.ContentTypeAsync("category");
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var region = await scenario.ContentTypeAsync("region");
        await scenario.PublishAsync(Create(product), Create(region));

        var original = await scenario.PublishAsync(Create(category), Update(product), Delete(region, 1));

        return (category, product, region, original);
    }

    private static async Task<Dictionary<(Guid ContentTypeId, int Version), Guid?>> VersionRowsAsync(ChangeSetScenario scenario)
    {
        var rows = await scenario.QueryAsync(context => context.ContentTypeVersions.AsNoTracking().ToListAsync());

        return rows.ToDictionary(row => (row.ContentTypeId, row.Version), row => (Guid?)row.Id);
    }
}
