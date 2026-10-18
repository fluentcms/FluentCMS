using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.ChangeSets;

// Content type writes as change set operations (SCH-05): each write and its operation commit together, and only publishing the
// set changes what is live.
public sealed class SchemaChangeSetTests
{
    private static readonly FieldDefinition Title = new("title", "text");

    private static readonly NewContentType Product = new("Product", "product", new ContentTypeDefinition([Title]));

    private static readonly ContentTypeDefinition WithSummary = new([Title, new FieldDefinition("summary", "text")]);

    private static readonly ContentTypeDefinition WithPrice = new([Title, new FieldDefinition("price", "number")]);

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_create_is_an_operation_of_its_set_and_is_live_only_once_the_set_publishes(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var changeSet = await scenario.OpenAsync();

        var created = (await scenario.RunSchemaAsync(directory => directory.CreateAsync(Product, ChangeSetTarget.Existing(changeSet.Id)))).Value;

        Assert.Equal([Create(created)], (await scenario.RunAsync(directory => directory.GetAsync(changeSet.Id))).Value.Operations);
        var events = await scenario.ChangeSetEventsAsync(changeSet.Id);
        Assert.Equal(["change_set.opened", "content_type.created", "change_set.operation_added"], events.Select(row => row.Operation));
        Assert.All(events.Skip(1), row => Assert.Equal(created.Id, row.SubjectId));

        var summary = Assert.Single(await scenario.RunSchemaAsync(directory => directory.ListAsync()));
        Assert.Equal((1, (int?)null), (summary.Version, summary.PublishedVersion));
        var live = await scenario.RunSchemaAsync(directory => directory.GetPublishedAsync("product"));
        Assert.Equal(SchemaErrorCodes.ContentTypeNotPublished, Assert.Single(live.Errors).Code);

        await scenario.PublishAsync(changeSet.Id);

        Assert.Equal(1, (await scenario.RunSchemaAsync(directory => directory.GetPublishedAsync("product"))).Value.Version);
        Assert.Equal(1, Assert.Single(await scenario.RunSchemaAsync(directory => directory.ListAsync())).PublishedVersion);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_in_the_same_set_extends_the_items_operation(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var changeSet = await scenario.OpenAsync();
        var target = ChangeSetTarget.Existing(changeSet.Id);
        var created = (await scenario.RunSchemaAsync(directory => directory.CreateAsync(Product, target))).Value;

        var revised = (await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithSummary, target))).Value;

        Assert.Equal(2, revised.Version);
        var operation = ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, created.Id, null, 2).Value;
        Assert.Equal([operation], (await scenario.RunAsync(directory => directory.GetAsync(changeSet.Id))).Value.Operations);
        Assert.Equal(2, Assert.Single(await scenario.OperationRowsAsync(changeSet.Id)).ProposedVersion);

        var events = await scenario.ChangeSetEventsAsync(changeSet.Id);
        Assert.Equal(
            ["change_set.opened", "content_type.created", "change_set.operation_added", "content_type.revised", "change_set.operation_revised"],
            events.Select(row => row.Operation));
        var firstVersion = await VersionRowAsync(scenario, created.Id, 1);
        Assert.Equal([firstVersion, firstVersion], events.Skip(3).Select(row => row.PreviousVersionId));

        var item = Assert.Single((await scenario.RunAsync(directory => directory.DiffAsync(changeSet.Id))).Value.Items);
        Assert.Equal(["title", "summary"], item.Fields.Select(field => field.Name));
        Assert.All(item.Fields, field => Assert.Equal(ValueChange.Added, field.Change));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_of_a_published_type_is_live_only_once_its_set_publishes(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var created = await PublishedProductAsync(scenario);
        var changeSet = await scenario.OpenAsync();

        var revised = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithSummary, ChangeSetTarget.Existing(changeSet.Id)));

        Assert.Equal(2, revised.Value.Version);
        Assert.Equal([Update(created, 1, 2)], (await scenario.RunAsync(directory => directory.GetAsync(changeSet.Id))).Value.Operations);
        Assert.Equal(2, (await scenario.RunSchemaAsync(directory => directory.GetAsync("product"))).Value.Version);
        Assert.Equal(1, (await scenario.RunSchemaAsync(directory => directory.GetPublishedAsync("product"))).Value.Version);

        await scenario.PublishAsync(changeSet.Id);

        var live = (await scenario.RunSchemaAsync(directory => directory.GetPublishedAsync("product"))).Value;
        Assert.Equal(2, live.Version);
        Assert.True(live.Definition.HasField("summary"));
        var previous = (await scenario.RunSchemaAsync(directory => directory.GetVersionAsync("product", 1))).Value;
        Assert.False(previous.Definition.HasField("summary"));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_based_on_the_published_version_leaves_an_unpublished_draft_behind(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var created = await PublishedProductAsync(scenario);
        var rejected = await scenario.OpenAsync();
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithSummary, ChangeSetTarget.Existing(rejected.Id)));
        await scenario.RunAsync(directory => directory.TransitionAsync(rejected.Id, ChangeSetState.ChecksRunning));
        await scenario.RunAsync(directory => directory.TransitionAsync(rejected.Id, ChangeSetState.AwaitingReview));
        await scenario.RunAsync(directory => directory.RejectAsync(rejected.Id, "The summary belongs on the page, not the product."));
        var changeSet = await scenario.OpenAsync();
        var target = ChangeSetTarget.Existing(changeSet.Id);

        var revised = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithPrice, target));
        var unknownBase = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 2, WithPrice, target));

        Assert.Equal(3, revised.Value.Version);
        Assert.Equal([Update(created, 1, 3)], (await scenario.RunAsync(directory => directory.GetAsync(changeSet.Id))).Value.Operations);
        Assert.Equal(SchemaErrorCodes.VersionConflict, Assert.Single(unknownBase.Errors).Code);

        await scenario.PublishAsync(changeSet.Id);

        var live = (await scenario.RunSchemaAsync(directory => directory.GetPublishedAsync("product"))).Value;
        Assert.Equal(3, live.Version);
        Assert.True(live.Definition.HasField("price"));
        Assert.False(live.Definition.HasField("summary"));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Writes_through_a_session_share_the_set_their_first_write_opens(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var target = ChangeSetTarget.Session("schema-session", CatalogSync);

        var product = (await scenario.RunSchemaAsync(directory => directory.CreateAsync(Product, target))).Value;
        var category = (await scenario.RunSchemaAsync(directory => directory.CreateAsync(new NewContentType("Category", "category", new ContentTypeDefinition([Title])), target))).Value;

        var stored = Assert.Single(await scenario.ChangeSetRowsAsync(), row => row.SessionId == "schema-session");
        Assert.Equal(CatalogSync.Rationale, stored.Rationale);
        Assert.Equal(Amir.UserId, stored.InitiatorId);
        Assert.Equal([Create(product), Create(category)], (await scenario.RunAsync(directory => directory.GetAsync(stored.Id))).Value.Operations);
    }

    // Expected failures are found before either directory opens its unit of work, so they never doom the caller's.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_refused_write_stores_nothing_and_leaves_the_callers_unit_of_work_able_to_commit(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var submitted = await scenario.OpenAsync();
        await scenario.RunSchemaAsync(directory => directory.CreateAsync(
            new NewContentType("Category", "category", new ContentTypeDefinition([Title])),
            ChangeSetTarget.Existing(submitted.Id)));
        await scenario.RunAsync(directory => directory.TransitionAsync(submitted.Id, ChangeSetState.ChecksRunning));
        var events = (await scenario.ChangeSetEventsAsync()).Count;
        Guid opened;

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            opened = (await scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>().OpenAsync(CatalogSync)).Value.Id;
            var unknown = await directory.CreateAsync(Product, ChangeSetTarget.Existing(Guid.NewGuid()));
            var notOpen = await directory.CreateAsync(Product, ChangeSetTarget.Existing(submitted.Id));
            var blank = await directory.CreateAsync(Product, ChangeSetTarget.Session("new-session", new NewChangeSet(" ", [])));

            Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Assert.Single(unknown.Errors).Code);
            Assert.Equal(ChangeSetErrorCodes.NotOpen, Assert.Single(notOpen.Errors).Code);
            Assert.Equal(ChangeSetErrorCodes.RationaleMissing, Assert.Single(blank.Errors).Code);
            await work.CommitAsync();
        }

        Assert.Equal(ChangeSetState.Open, (await scenario.ChangeSetRowAsync(opened)).State);
        Assert.DoesNotContain(await scenario.RunSchemaAsync(directory => directory.ListAsync()), type => type.Slug == "product");
        Assert.Single(await scenario.OperationRowsAsync(submitted.Id));
        Assert.Equal(events + 1, (await scenario.ChangeSetEventsAsync()).Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_writes_only_into_its_own_set_and_within_its_cap(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var persons = await scenario.AsPersonAsync(directory => directory.OpenAsync(CatalogSync));
        var own = await scenario.OpenAsync();
        await scenario.UpdateAgentAsync(row => row.ItemsPerChangeSet = 1);

        var intoPersons = await scenario.RunSchemaAsync(directory => directory.CreateAsync(Product, ChangeSetTarget.Existing(persons.Value.Id)));
        var first = await scenario.RunSchemaAsync(directory => directory.CreateAsync(Product, ChangeSetTarget.Existing(own.Id)));
        var pastCap = await scenario.RunSchemaAsync(directory => directory.CreateAsync(
            new NewContentType("Category", "category", new ContentTypeDefinition([Title])),
            ChangeSetTarget.Existing(own.Id)));

        Assert.Equal(AgentPermissionErrorCodes.NotOwner, Assert.Single(intoPersons.Errors).Code);
        Assert.True(first.IsSuccess);
        Assert.Equal(AgentPermissionErrorCodes.CapExceeded, Assert.Single(pastCap.Errors).Code);
        Assert.Equal(["product"], (await scenario.RunSchemaAsync(directory => directory.ListAsync())).Select(type => type.Slug));
        Assert.Empty(await scenario.OperationRowsAsync(persons.Value.Id));
        Assert.Single(await scenario.OperationRowsAsync(own.Id));
    }

    // A revision moves the latest version and the set's revision before its events are appended, so each failure point comes
    // after something reached the database.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failure_mid_write_leaves_neither_the_version_nor_the_operation(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var changeSet = await scenario.OpenAsync();
        var target = ChangeSetTarget.Existing(changeSet.Id);
        var created = (await scenario.RunSchemaAsync(directory => directory.CreateAsync(Product, target))).Value;
        var revision = (await scenario.ChangeSetRowAsync(changeSet.Id)).Revision;
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        for (var append = 1; append <= 2; append++)
        {
            await using (var scope = scenario.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
                await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

                scenario.FailOnAppend(append);
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>().ReviseAsync("product", 1, WithSummary, target));

                Assert.Equal(2, await context.ContentTypes.AsNoTracking().Where(row => row.Id == created.Id).Select(row => row.Version).SingleAsync());
            }

            Assert.Equal(1, (await scenario.RunSchemaAsync(directory => directory.GetAsync("product"))).Value.Version);
            Assert.Single(await scenario.QueryAsync(context => context.ContentTypeVersions.AsNoTracking().Where(row => row.ContentTypeId == created.Id).ToListAsync()));
            Assert.Equal(1, Assert.Single(await scenario.OperationRowsAsync(changeSet.Id)).ProposedVersion);
            Assert.Equal(revision, (await scenario.ChangeSetRowAsync(changeSet.Id)).Revision);
            Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);
        }

        Assert.True((await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithSummary, target))).IsSuccess);
    }

    // Content type writes here are a person's, who also makes the review moves (MCP-06).
    private static async Task<ChangeSetScenario> ScenarioAsync(string provider)
    {
        var scenario = await ChangeSetScenario.CreateAsync(provider);
        scenario.Initiator = Amir;
        return scenario;
    }

    private static async Task<ContentType> PublishedProductAsync(ChangeSetScenario scenario)
    {
        var changeSet = await scenario.OpenAsync();
        var created = (await scenario.RunSchemaAsync(directory => directory.CreateAsync(Product, ChangeSetTarget.Existing(changeSet.Id)))).Value;
        await scenario.PublishAsync(changeSet.Id);

        return created;
    }

    private static Task<Guid?> VersionRowAsync(ChangeSetScenario scenario, Guid contentTypeId, int version)
    {
        return scenario.QueryAsync(context => context.ContentTypeVersions
            .AsNoTracking()
            .Where(row => row.ContentTypeId == contentTypeId && row.Version == version)
            .Select(row => (Guid?)row.Id)
            .SingleAsync());
    }
}
