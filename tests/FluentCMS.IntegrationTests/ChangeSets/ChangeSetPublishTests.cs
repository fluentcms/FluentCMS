using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Persistence;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.ChangeSets;

public sealed class ChangeSetPublishTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Publishing_an_approved_set_moves_every_item_and_records_its_provenance(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var category = await scenario.ContentTypeAsync("category");
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var productVersions = await scenario.QueryAsync(context => context.ContentTypeVersions
            .AsNoTracking()
            .Where(version => version.ContentTypeId == product.Id)
            .OrderBy(version => version.Version)
            .Select(version => version.Id)
            .ToListAsync());

        // Moving a set to published publishes it like PublishAsync does.
        var first = await scenario.ApproveAsync(Create(product));
        Assert.True((await scenario.RunAsync(directory => directory.TransitionAsync(first.Id, ChangeSetState.Published))).IsSuccess);
        Assert.Equal(1, await scenario.PublishedVersionAsync(product.Id));

        var changeSet = await scenario.ApproveAsync(Create(category), Update(product));
        var before = (await scenario.ChangeSetEventsAsync(changeSet.Id)).Count;

        var published = await scenario.RunAsync(directory => directory.PublishAsync(changeSet.Id));

        Assert.True(published.IsSuccess, string.Join("; ", published.Errors.Select(error => error.Message)));
        Assert.Equal(ChangeSetState.Published, published.Value.State);
        Assert.Equal(ChangeSetState.Published, (await scenario.ChangeSetRowAsync(changeSet.Id)).State);
        Assert.Equal(1, await scenario.PublishedVersionAsync(category.Id));
        Assert.Equal(2, await scenario.PublishedVersionAsync(product.Id));

        var events = (await scenario.ChangeSetEventsAsync(changeSet.Id)).Skip(before).ToList();
        Assert.Equal(["content_type.published", "content_type.published", "change_set.published"], events.Select(row => row.Operation));
        Assert.Equal(["content_type", "content_type", "change_set"], events.Select(row => row.SubjectType));
        Assert.Equal([category.Id, product.Id, changeSet.Id], events.Select(row => row.SubjectId));
        Assert.Equal([null, productVersions[0], null], events.Select(row => row.PreviousVersionId));
        Assert.All(events, row => Assert.Equal(changeSet.Id, row.ChangeSetId));
        Assert.All(events, row => Assert.Equal(InitiatorKinds.User, row.InitiatorKind));
        Assert.All(events, row => Assert.Equal(Amir.UserId, row.InitiatorId));
        Assert.All(events.Take(2), row => Assert.Equal(CatalogSync.Sources, row.Sources));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_published_delete_clears_the_published_version(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var product = await scenario.ContentTypeAsync("product");
        await scenario.PublishAsync(Create(product));
        var publishedRow = await scenario.QueryAsync(context => context.ContentTypeVersions
            .AsNoTracking()
            .Where(version => version.ContentTypeId == product.Id && version.Version == 1)
            .Select(version => version.Id)
            .SingleAsync());

        var deleted = await scenario.PublishAsync(Delete(product, 1));

        Assert.Null(await scenario.PublishedVersionAsync(product.Id));
        var unpublished = (await scenario.ChangeSetEventsAsync(deleted.Id))[^2];
        Assert.Equal("content_type.unpublished", unpublished.Operation);
        Assert.Equal(product.Id, unpublished.SubjectId);
        Assert.Equal(publishedRow, unpublished.PreviousVersionId);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Only_an_approved_set_publishes(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var category = await scenario.ContentTypeAsync("category");
        var product = await scenario.ContentTypeAsync("product");
        var open = await scenario.OpenAsync(Create(product));
        var reviewed = await scenario.OpenAsync(Create(product));
        await scenario.RunAsync(directory => directory.TransitionAsync(reviewed.Id, ChangeSetState.ChecksRunning));
        await scenario.RunAsync(directory => directory.TransitionAsync(reviewed.Id, ChangeSetState.AwaitingReview));
        var published = await scenario.PublishAsync(Create(category));
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        var fromOpen = await scenario.RunAsync(directory => directory.PublishAsync(open.Id));
        var fromReview = await scenario.RunAsync(directory => directory.PublishAsync(reviewed.Id));
        var again = await scenario.RunAsync(directory => directory.PublishAsync(published.Id));
        var unknown = await scenario.RunAsync(directory => directory.PublishAsync(Guid.NewGuid()));

        Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(fromOpen.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(fromReview.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(again.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Assert.Single(unknown.Errors).Code);
        Assert.Null(await scenario.PublishedVersionAsync(product.Id));
        Assert.Equal(ChangeSetState.Open, (await scenario.ChangeSetRowAsync(open.Id)).State);
        Assert.Equal(ChangeSetState.AwaitingReview, (await scenario.ChangeSetRowAsync(reviewed.Id)).State);
        Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_set_whose_items_moved_past_their_base_versions_is_refused_and_writes_nothing(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var category = await scenario.ContentTypeAsync("category", revised: true);
        await scenario.PublishAsync(Create(product));
        var stale = await scenario.ApproveAsync(Update(product), Update(category));
        var recreate = await scenario.ApproveAsync(Create(product));
        await scenario.PublishAsync(Update(product));
        var revision = (await scenario.ChangeSetRowAsync(stale.Id)).Revision;
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        var refused = await scenario.RunAsync(directory => directory.PublishAsync(stale.Id));
        var recreated = await scenario.RunAsync(directory => directory.PublishAsync(recreate.Id));

        Assert.All(refused.Errors.Concat(recreated.Errors), error => Assert.Equal(ChangeSetErrorCodes.BaseVersionStale, error.Code));
        Assert.Equal(
            [
                "The content type 'product' is published at version 2, but this change set was based on version 1.",
                "The content type 'category' is not published, but this change set was based on version 1.",
            ],
            refused.Errors.Select(error => error.Message));
        Assert.Equal(
            "The content type 'product' is published at version 2, but this change set creates it.",
            Assert.Single(recreated.Errors).Message);

        Assert.Equal(2, await scenario.PublishedVersionAsync(product.Id));
        Assert.Null(await scenario.PublishedVersionAsync(category.Id));
        var after = await scenario.ChangeSetRowAsync(stale.Id);
        Assert.Equal(ChangeSetState.Approved, after.State);
        Assert.Equal(revision, after.Revision);
        Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);

        // A set based on the version published now goes through.
        var next = await scenario.ReviseAsync(product);
        await scenario.PublishAsync(Update(next, 2, 3));
        Assert.Equal(3, await scenario.PublishedVersionAsync(product.Id));
    }

    // The publish appends one event per item and then the set's, each after the first item's version moved in the transaction.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failure_mid_publish_leaves_nothing_published_and_the_set_approved(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var category = await scenario.ContentTypeAsync("category");
        var product = await scenario.ContentTypeAsync("product", revised: true);
        await scenario.PublishAsync(Create(product));
        var changeSet = await scenario.ApproveAsync(Create(category), Update(product));
        var revision = (await scenario.ChangeSetRowAsync(changeSet.Id)).Revision;
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        async Task AssertNothingPublishedAsync()
        {
            Assert.Null(await scenario.PublishedVersionAsync(category.Id));
            Assert.Equal(1, await scenario.PublishedVersionAsync(product.Id));
            var stored = await scenario.ChangeSetRowAsync(changeSet.Id);
            Assert.Equal(ChangeSetState.Approved, stored.State);
            Assert.Equal(revision, stored.Revision);
            Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);
        }

        for (var append = 1; append <= 3; append++)
        {
            scenario.FailOnAppend(append);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.RunAsync(directory => directory.PublishAsync(changeSet.Id)));
            await AssertNothingPublishedAsync();
        }

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            scenario.FailOnAppend(3);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>().PublishAsync(changeSet.Id));

            // Inside the still open transaction every version has moved, so the failure came after they reached the database.
            Assert.Equal(
                [1, 2],
                await context.ContentTypes
                    .AsNoTracking()
                    .Where(row => row.Id == category.Id || row.Id == product.Id)
                    .OrderBy(row => row.Slug)
                    .Select(row => row.PublishedVersion)
                    .ToListAsync());
        }

        await AssertNothingPublishedAsync();
        Assert.True((await scenario.RunAsync(directory => directory.PublishAsync(changeSet.Id))).IsSuccess);
        Assert.Equal(1, await scenario.PublishedVersionAsync(category.Id));
        Assert.Equal(2, await scenario.PublishedVersionAsync(product.Id));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_two_concurrent_publishes_over_one_item_one_wins_and_the_other_is_stale(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        await scenario.PublishAsync(Create(product));
        var winner = await scenario.ApproveAsync(Update(product));
        var loser = await scenario.ApproveAsync(Update(product));
        var loserEvents = (await scenario.ChangeSetEventsAsync(loser.Id)).Count;

        var lost = await scenario.RaceAsync(
            directory => directory.PublishAsync(winner.Id),
            directory => directory.PublishAsync(loser.Id));

        Assert.Equal(ChangeSetErrorCodes.BaseVersionStale, Assert.Single(lost.Errors).Code);
        Assert.Equal(2, await scenario.PublishedVersionAsync(product.Id));
        Assert.Equal(ChangeSetState.Published, (await scenario.ChangeSetRowAsync(winner.Id)).State);
        Assert.Equal(ChangeSetState.Approved, (await scenario.ChangeSetRowAsync(loser.Id)).State);
        Assert.Equal(loserEvents, (await scenario.ChangeSetEventsAsync(loser.Id)).Count);
    }

    // Items move in one order whatever order the sets list them in, so the later publish waits for the earlier one and finds its
    // base gone, instead of the two deadlocking on each other's rows.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Two_sets_that_list_the_same_items_in_opposite_orders_publish_at_once_and_one_is_stale(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        for (var round = 0; round < 5; round++)
        {
            var product = await scenario.ContentTypeAsync($"product-{round}", revised: true);
            var category = await scenario.ContentTypeAsync($"category-{round}", revised: true);
            await scenario.PublishAsync(Create(product), Create(category));
            var forward = await scenario.ApproveAsync(Update(product), Update(category));
            var backward = await scenario.ApproveAsync(Update(category), Update(product));

            var results = await Task.WhenAll(
                Task.Run(() => scenario.RunAsync(directory => directory.PublishAsync(forward.Id))),
                Task.Run(() => scenario.RunAsync(directory => directory.PublishAsync(backward.Id))));

            Assert.Single(results, result => result.IsSuccess);
            var lost = Assert.Single(results, result => !result.IsSuccess);
            Assert.NotEmpty(lost.Errors);
            Assert.All(lost.Errors, error => Assert.Equal(ChangeSetErrorCodes.BaseVersionStale, error.Code));
            Assert.Equal(2, await scenario.PublishedVersionAsync(product.Id));
            Assert.Equal(2, await scenario.PublishedVersionAsync(category.Id));
        }
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_two_concurrent_publishes_of_one_set_one_wins_and_the_other_gets_a_conflict(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var product = await scenario.ContentTypeAsync("product");
        var changeSet = await scenario.ApproveAsync(Create(product));

        var lost = await scenario.RaceAsync(
            directory => directory.PublishAsync(changeSet.Id),
            directory => directory.PublishAsync(changeSet.Id));

        Assert.Equal(ChangeSetErrorCodes.Conflict, Assert.Single(lost.Errors).Code);
        Assert.Equal(1, await scenario.PublishedVersionAsync(product.Id));
        var operations = (await scenario.ChangeSetEventsAsync(changeSet.Id)).Select(row => row.Operation).ToList();
        Assert.Single(operations, operation => operation == "content_type.published");
        Assert.Single(operations, operation => operation == "change_set.published");
    }

    // Expected failures are found before the directory opens its own unit of work, so they never doom the caller's.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_refused_publish_leaves_the_callers_unit_of_work_able_to_commit(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var stale = await scenario.ApproveAsync(Update(product));
        Guid id;

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            id = (await directory.OpenAsync(CatalogSync)).Value.Id;
            var refusedStale = await directory.PublishAsync(stale.Id);
            var refusedOpen = await directory.PublishAsync(id);

            Assert.Equal(ChangeSetErrorCodes.BaseVersionStale, Assert.Single(refusedStale.Errors).Code);
            Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(refusedOpen.Errors).Code);
            await work.CommitAsync();
        }

        Assert.Equal(ChangeSetState.Open, (await scenario.ChangeSetRowAsync(id)).State);
        Assert.Equal(ChangeSetState.Approved, (await scenario.ChangeSetRowAsync(stale.Id)).State);
        Assert.Null(await scenario.PublishedVersionAsync(product.Id));
    }

    // Approving and publishing are review moves, which only a person makes (MCP-06).
    private static async Task<ChangeSetScenario> ScenarioAsync(string provider)
    {
        var scenario = await ChangeSetScenario.CreateAsync(provider);
        scenario.Initiator = Amir;
        return scenario;
    }
}