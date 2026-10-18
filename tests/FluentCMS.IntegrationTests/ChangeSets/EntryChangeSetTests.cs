using System.Text.Json;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.ChangeSets;

public sealed class EntryChangeSetTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Entry_and_entity_operations_point_at_stored_versions_of_entries_of_their_kind(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, product) = await CatalogAsync(scenario);
        var draft = (await EntriesAsync(scenario, entries => entries.CreateAsync(new NewEntry("page", Json("""{"title": "Draft"}"""))))).Value.Id;
        await EntriesAsync(scenario, entries => entries.DeleteAsync(draft, 1));

        var changeSet = await scenario.OpenAsync(Operation(OperationKind.Create, ItemKind.Entry, page, null, 1), Operation(OperationKind.Create, ItemKind.Entity, product, null, 1));

        Assert.Equal([ItemKind.Entry, ItemKind.Entity], (await scenario.OperationRowsAsync(changeSet.Id)).Select(row => row.ItemKind));
        var added = (await scenario.ChangeSetEventsAsync(changeSet.Id)).Where(row => row.Operation == "change_set.operation_added").ToList();
        Assert.Equal(["entry", "entity"], added.Select(row => row.SubjectType));
        Assert.Equal([page, product], added.Select(row => row.SubjectId!.Value));

        (ChangeOperation Operation, string Code)[] cases =
        [
            (Operation(OperationKind.Update, ItemKind.Entity, page, 1, 2), ChangeSetErrorCodes.ItemKindMismatch),
            (Operation(OperationKind.Create, ItemKind.Entry, product, null, 1), ChangeSetErrorCodes.ItemKindMismatch),
            (Operation(OperationKind.Create, ItemKind.Entry, Guid.NewGuid(), null, 1), ChangeSetErrorCodes.ItemNotFound),
            (Operation(OperationKind.Create, ItemKind.Entry, draft, null, 1), ChangeSetErrorCodes.ItemNotFound),
        ];

        var other = await scenario.OpenAsync();
        foreach (var (operation, code) in cases)
        {
            var result = await scenario.RunAsync(directory => directory.AddOperationAsync(other.Id, operation));
            Assert.Equal(code, Assert.Single(result.Errors).Code);
        }

        var missing = await scenario.RunAsync(directory => directory.AddOperationAsync(other.Id, Operation(OperationKind.Update, ItemKind.Entry, page, 1, 3)));
        Assert.Equal([ChangeSetErrorCodes.VersionNotFound], missing.Errors.Select(error => error.Code));
        Assert.Empty(await scenario.OperationRowsAsync(other.Id));

        await UpdatePageAsync(scenario, page, 1, "Widget Pro");
        await scenario.RunAsync(directory => directory.AddOperationAsync(other.Id, Operation(OperationKind.Update, ItemKind.Entry, page, 1, 2)));
        var update = Assert.Single(await scenario.ChangeSetEventsAsync(other.Id), row => row.Operation == "change_set.operation_added");
        Assert.Equal(await VersionRowIdAsync(scenario, page, 1), update.PreviousVersionId);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_puts_only_entries_of_types_in_its_scope_in_a_change_set(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, product) = await CatalogAsync(scenario);
        await scenario.UpdateAgentAsync(row => row.Types = ["page"]);
        scenario.Initiator = Agent;
        var changeSet = await scenario.OpenAsync(Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));

        var outside = await scenario.RunAsync(directory => directory.AddOperationAsync(changeSet.Id, Operation(OperationKind.Create, ItemKind.Entity, product, null, 1)));

        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Assert.Single(outside.Errors).Code);
        Assert.Single(await scenario.OperationRowsAsync(changeSet.Id));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_diff_of_entry_operations_lists_their_fields(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, product) = await CatalogAsync(scenario);
        await UpdatePageAsync(scenario, page, 1, "Widget Pro");

        var changeSet = await scenario.OpenAsync(
            Operation(OperationKind.Update, ItemKind.Entry, page, 1, 2),
            Operation(OperationKind.Create, ItemKind.Entity, product, null, 1));
        var diff = (await scenario.RunAsync(directory => directory.DiffAsync(changeSet.Id))).Value;

        var title = Assert.Single(diff.Items[0].Fields);
        Assert.Equal(("title", ValueChange.Changed), (title.Name, title.Change));
        Assert.Equal("Widget", title.Before!.Value.GetString());
        Assert.Equal("Widget Pro", title.After!.Value.GetString());
        Assert.Equal([("name", ValueChange.Added), ("price", ValueChange.Added)], diff.Items[1].Fields.Select(field => (field.Name, field.Change)));
        Assert.Equal(19.99m, diff.Items[1].Fields[1].After!.Value.GetProperty("value").GetDecimal());
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Publishing_moves_entries_published_versions_and_resolved_views_follow(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, product) = await CatalogAsync(scenario);

        var first = await scenario.PublishAsync(
            Operation(OperationKind.Create, ItemKind.Entry, page, null, 1),
            Operation(OperationKind.Create, ItemKind.Entity, product, null, 1));

        Assert.Equal(1, await PublishedVersionOfAsync(scenario, page));
        Assert.Equal(1, await PublishedVersionOfAsync(scenario, product));
        Assert.Equal(19.99m, (await ResolvedPriceAsync(scenario, page)).GetDecimal());
        var events = (await scenario.ChangeSetEventsAsync(first.Id)).Where(row => row.Operation.EndsWith(".published", StringComparison.Ordinal)).ToList();
        Assert.Equal(["entry.published", "entity.published", "change_set.published"], events.Select(row => row.Operation));
        Assert.Equal([page, product, first.Id], events.Select(row => row.SubjectId!.Value));

        await EntriesAsync(scenario, entries => entries.UpdateAsync(product, 1, Json(ProductFields("24.99"))));
        Assert.Equal(19.99m, (await ResolvedPriceAsync(scenario, page)).GetDecimal());

        var second = await scenario.PublishAsync(Operation(OperationKind.Update, ItemKind.Entity, product, 1, 2));

        Assert.Equal(2, await PublishedVersionOfAsync(scenario, product));
        Assert.Equal(24.99m, (await ResolvedPriceAsync(scenario, page)).GetDecimal());
        var published = Assert.Single(await scenario.ChangeSetEventsAsync(second.Id), row => row.Operation == "entity.published");
        Assert.Equal(await VersionRowIdAsync(scenario, product, 1), published.PreviousVersionId);

        // The entity's own events and the change set events about it are one history.
        var history = await scenario.QueryAsync(context => context.AuditEvents
            .AsNoTracking()
            .Where(row => row.SubjectType == "entity" && row.SubjectId == product)
            .Select(row => row.Operation)
            .ToListAsync());
        Assert.Equal(
            ["change_set.operation_added", "change_set.operation_added", "entity.created", "entity.published", "entity.published", "entity.updated"],
            history.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_stale_base_or_a_deleted_entry_fails_the_publish_and_moves_nothing(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, _) = await CatalogAsync(scenario);
        var winner = await scenario.ApproveAsync(Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));
        var loser = await scenario.ApproveAsync(Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));

        Assert.True((await scenario.RunAsync(directory => directory.PublishAsync(winner.Id))).IsSuccess);
        var stale = await scenario.RunAsync(directory => directory.PublishAsync(loser.Id));

        Assert.Equal(ChangeSetErrorCodes.BaseVersionStale, Assert.Single(stale.Errors).Code);
        Assert.Equal($"The entry {page} is published at version 1, but this change set creates it.", stale.Errors[0].Message);
        Assert.Equal(ChangeSetState.Approved, (await scenario.ChangeSetRowAsync(loser.Id)).State);

        var unpublish = await scenario.ApproveAsync(Operation(OperationKind.Delete, ItemKind.Entry, page, 1, null));
        await EntriesAsync(scenario, entries => entries.DeleteAsync(page, 1));
        var deleted = await scenario.RunAsync(directory => directory.PublishAsync(unpublish.Id));

        Assert.Equal(ChangeSetErrorCodes.ItemNotFound, Assert.Single(deleted.Errors).Code);
        Assert.Equal(1, await PublishedVersionOfAsync(scenario, page));
        Assert.Equal(ChangeSetState.Approved, (await scenario.ChangeSetRowAsync(unpublish.Id)).State);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_published_delete_unpublishes_the_entry_and_its_revert_publishes_it_again(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, _) = await CatalogAsync(scenario);
        await scenario.PublishAsync(Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));

        var delete = await scenario.PublishAsync(Operation(OperationKind.Delete, ItemKind.Entry, page, 1, null));

        Assert.Null(await PublishedVersionOfAsync(scenario, page));
        Assert.Equal(1, (await EntriesAsync(scenario, entries => entries.GetAsync(page))).Value.LatestVersion);
        Assert.Single(await scenario.ChangeSetEventsAsync(delete.Id), row => row.Operation == "entry.unpublished");

        var revert = (await scenario.RunAsync(directory => directory.RevertAsync(delete.Id, "The page is still needed."))).Value;
        await scenario.PublishAsync(revert.Id);

        Assert.Equal(1, await PublishedVersionOfAsync(scenario, page));
        Assert.Equal(ChangeSetState.Reverted, (await scenario.ChangeSetRowAsync(delete.Id)).State);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Reverting_an_update_puts_the_earlier_version_back(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, product) = await CatalogAsync(scenario);
        await scenario.PublishAsync(Operation(OperationKind.Create, ItemKind.Entry, page, null, 1), Operation(OperationKind.Create, ItemKind.Entity, product, null, 1));
        await UpdatePageAsync(scenario, page, 1, "Widget Pro");
        var update = await scenario.PublishAsync(Operation(OperationKind.Update, ItemKind.Entry, page, 1, 2));

        var revert = (await scenario.RunAsync(directory => directory.RevertAsync(update.Id, "The new title was wrong."))).Value;
        var diff = (await scenario.RunAsync(directory => directory.DiffAsync(revert.Id))).Value;
        await scenario.PublishAsync(revert.Id);

        Assert.Equal("Widget Pro", Assert.Single(diff.Items[0].Fields).Before!.Value.GetString());
        Assert.Equal(1, await PublishedVersionOfAsync(scenario, page));
        Assert.Equal(2, (await EntriesAsync(scenario, entries => entries.GetAsync(page))).Value.LatestVersion);
        Assert.Equal(ChangeSetState.Reverted, (await scenario.ChangeSetRowAsync(update.Id)).State);
    }

    // The publish appends one event per item and then the set's, each after the first item's pointer moved in the transaction.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failure_mid_publish_leaves_no_entry_published(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, product) = await CatalogAsync(scenario);
        var changeSet = await scenario.ApproveAsync(
            Operation(OperationKind.Create, ItemKind.Entry, page, null, 1),
            Operation(OperationKind.Create, ItemKind.Entity, product, null, 1));

        for (var append = 1; append <= 3; append++)
        {
            scenario.FailOnAppend(append);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.RunAsync(directory => directory.PublishAsync(changeSet.Id)));

            Assert.Null(await PublishedVersionOfAsync(scenario, page));
            Assert.Null(await PublishedVersionOfAsync(scenario, product));
            Assert.Equal(ChangeSetState.Approved, (await scenario.ChangeSetRowAsync(changeSet.Id)).State);
        }

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            scenario.FailOnAppend(3);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>().PublishAsync(changeSet.Id));

            // Inside the still open transaction both pointers have moved, so the failure came after they reached the database.
            Assert.Equal([1, 1], await context.Entries.AsNoTracking().Where(row => row.Id == page || row.Id == product).Select(row => row.PublishedVersion).ToListAsync());
        }

        Assert.Null(await PublishedVersionOfAsync(scenario, page));
        Assert.True((await scenario.RunAsync(directory => directory.PublishAsync(changeSet.Id))).IsSuccess);
        Assert.Equal(1, await PublishedVersionOfAsync(scenario, page));
    }

    private static async Task<ChangeSetScenario> ScenarioAsync(string provider)
    {
        var scenario = await ChangeSetScenario.CreateAsync(provider);
        scenario.Initiator = Amir;
        return scenario;
    }

    // A product entity at 19.99 and a page that shows its price, each at version 1 and unpublished.
    private static async Task<(Guid Page, Guid Product)> CatalogAsync(ChangeSetScenario scenario)
    {
        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var types = scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>();
            var product = new ContentTypeDefinition([new FieldDefinition("name", "text"), new FieldDefinition("price", "number")], isEntity: true);
            var page = new ContentTypeDefinition(
            [
                new FieldDefinition("title", "text"),
                new FieldDefinition("price", "reference", settings: Json("""{"target": "product", "field": "price"}""")),
            ]);

            Assert.True((await types.CreateAsync(new NewContentType("Product", "product", product), TestChangeSetTargets.ContentModel)).IsSuccess);
            Assert.True((await types.CreateAsync(new NewContentType("Page", "page", page), TestChangeSetTargets.ContentModel)).IsSuccess);
        }

        var productId = (await EntriesAsync(scenario, entries => entries.CreateAsync(new NewEntry("product", Json(ProductFields("19.99")))))).Value.Id;
        var pageId = (await EntriesAsync(scenario, entries => entries.CreateAsync(new NewEntry("page", Json($$"""{"title": "Widget", "price": "{{productId}}"}"""))))).Value.Id;

        return (pageId, productId);
    }

    private static string ProductFields(string price)
    {
        static string Fact(string value) =>
            $$"""{"value": {{value}}, "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}""";

        return $$"""{"name": {{Fact("\"Widget\"")}}, "price": {{Fact(price)}}}""";
    }

    private static async Task UpdatePageAsync(ChangeSetScenario scenario, Guid page, int baseVersion, string title)
    {
        var current = (await EntriesAsync(scenario, entries => entries.GetAsync(page))).Value.Latest.Fields;
        var price = current.GetProperty("price").GetString();
        var updated = await EntriesAsync(scenario, entries => entries.UpdateAsync(page, baseVersion, Json($$"""{"title": "{{title}}", "price": "{{price}}"}""")));
        Assert.True(updated.IsSuccess, string.Join("; ", updated.Errors.Select(error => error.Message)));
    }

    private static async Task<T> EntriesAsync<T>(ChangeSetScenario scenario, Func<IEntryDirectory, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IEntryDirectory>());
    }

    private static async Task<JsonElement> ResolvedPriceAsync(ChangeSetScenario scenario, Guid page)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var resolved = await scope.ServiceProvider.GetRequiredService<IResolvedEntries>().ResolveAsync(page);
        return resolved.Value.Fields.GetProperty("price");
    }

    private static Task<int?> PublishedVersionOfAsync(ChangeSetScenario scenario, Guid entry)
    {
        return scenario.QueryAsync(context => context.Entries.AsNoTracking().Where(row => row.Id == entry).Select(row => row.PublishedVersion).SingleAsync());
    }

    private static Task<Guid> VersionRowIdAsync(ChangeSetScenario scenario, Guid entry, int version)
    {
        return scenario.QueryAsync(context => context.EntryVersions
            .AsNoTracking()
            .Where(row => row.EntryId == entry && row.Version == version)
            .Select(row => row.Id)
            .SingleAsync());
    }

    private static ChangeOperation Operation(OperationKind kind, ItemKind itemKind, Guid item, int? baseVersion, int? proposedVersion)
    {
        return ChangeOperation.Create(kind, itemKind, item, baseVersion, proposedVersion).Value;
    }

    private static JsonElement Json(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
