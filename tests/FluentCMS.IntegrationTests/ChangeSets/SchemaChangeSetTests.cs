using System.Text.Json;
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

    private static readonly FieldDefinition Name = new("name", "text");

    private static readonly FieldDefinition Logo = new("logo", "text");

    private static readonly FieldDefinition BrandLogo = new("brandLogo", "reference", settings: Json("""{"target": "brand", "field": "logo"}"""));

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
            ["change_set.opened", "content_type.created", "change_set.operation_added", "content_type.revised", "change_set.operation_updated"],
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

        // The rejected draft is still the latest version, but no set that can publish proposes it, so it is no base.
        var deadDraft = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 2, WithPrice, target));
        var revised = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithPrice, target));
        var unknownBase = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 2, WithPrice, target));

        var refusal = Assert.Single(deadDraft.Errors);
        Assert.Equal(SchemaErrorCodes.VersionConflict, refusal.Code);
        Assert.Contains("revise the published version 1", refusal.Message);
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
        Assert.Equal(
            ["change_set.opened", "content_type.created", "change_set.operation_added", "content_type.created", "change_set.operation_added"],
            (await scenario.ChangeSetEventsAsync(stored.Id)).Select(row => row.Operation));
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

    // A refusal is found before the directories write, so the caller's unit of work still commits; a refused cap keeps its
    // security event (SEC-08), and nothing else of a refused write is stored.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_writes_only_into_its_own_set_and_within_its_cap(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        scenario.Initiator = OtherAgent;
        var others = await scenario.OpenAsync();
        scenario.Initiator = Agent;
        var own = await scenario.OpenAsync();
        await scenario.UpdateAgentAsync(row => row.ItemsPerChangeSet = 1);
        var writes = await WriteEventsAsync(scenario);

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            var first = await directory.CreateAsync(Product, ChangeSetTarget.Existing(own.Id));
            var intoOthers = await directory.CreateAsync(new NewContentType("Region", "region", new ContentTypeDefinition([Title])), ChangeSetTarget.Existing(others.Id));
            var pastCap = await directory.CreateAsync(new NewContentType("Category", "category", new ContentTypeDefinition([Title])), ChangeSetTarget.Existing(own.Id));

            Assert.True(first.IsSuccess);
            Assert.Equal(AgentPermissionErrorCodes.NotOwner, Assert.Single(intoOthers.Errors).Code);
            Assert.Equal(AgentPermissionErrorCodes.CapExceeded, Assert.Single(pastCap.Errors).Code);
            await work.CommitAsync();
        }

        Assert.Equal(["product"], (await scenario.RunSchemaAsync(directory => directory.ListAsync())).Select(type => type.Slug));
        Assert.Empty(await scenario.OperationRowsAsync(others.Id));
        Assert.Single(await scenario.OperationRowsAsync(own.Id));
        Assert.Equal(["content_type.created", "change_set.operation_added"], (await WriteEventsAsync(scenario)).Skip(writes.Count));
        Assert.Single(
            await scenario.QueryAsync(context => context.AuditEvents.AsNoTracking().Where(row => row.Operation == "agent.items_per_change_set_exceeded").ToListAsync()));
    }

    // A draft an open set proposes is a base for the next draft, which publishes after it.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_may_build_on_a_draft_an_open_set_still_proposes(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var created = await PublishedProductAsync(scenario);
        var first = await scenario.OpenAsync();
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithSummary, ChangeSetTarget.Existing(first.Id)));
        var second = await scenario.OpenAsync();

        var chained = await scenario.RunSchemaAsync(directory => directory.ReviseAsync(
            "product",
            2,
            new ContentTypeDefinition([Title, new FieldDefinition("summary", "text"), new FieldDefinition("price", "number")]),
            ChangeSetTarget.Existing(second.Id)));

        Assert.Equal(3, chained.Value.Version);
        Assert.Equal([Update(created, 2, 3)], (await scenario.RunAsync(directory => directory.GetAsync(second.Id))).Value.Operations);
        await scenario.PublishAsync(first.Id);
        await scenario.PublishAsync(second.Id);
        Assert.Equal(3, (await scenario.RunSchemaAsync(directory => directory.GetPublishedAsync("product"))).Value.Version);
    }

    // A set that already holds a type builds on its own draft, whatever other sets have drafted since.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_set_revises_its_own_draft_after_another_set_drafted_the_type(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var created = await PublishedProductAsync(scenario);
        var first = await scenario.OpenAsync();
        var target = ChangeSetTarget.Existing(first.Id);
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithSummary, target));
        var other = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithPrice, other));
        var both = new ContentTypeDefinition([Title, new FieldDefinition("summary", "text"), new FieldDefinition("price", "number")]);

        var fromLatest = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 3, both, target));
        var fromOwn = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 2, both, target));

        var refusal = Assert.Single(fromLatest.Errors);
        Assert.Equal(SchemaErrorCodes.VersionConflict, refusal.Code);
        Assert.Contains("already proposes version 2", refusal.Message);
        Assert.Equal(4, fromOwn.Value.Version);
        Assert.Equal([Update(created, 1, 4)], (await scenario.RunAsync(directory => directory.GetAsync(first.Id))).Value.Operations);
        await scenario.PublishAsync(first.Id);
        Assert.Equal(4, (await scenario.RunSchemaAsync(directory => directory.GetPublishedAsync("product"))).Value.Version);
    }

    // A revision of a type with nothing live is recorded as its create, so an agent needs the create scope for it (MCP-05).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_without_the_create_scope_cannot_revise_a_type_with_nothing_live(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        await scenario.ContentTypeAsync("product");
        var own = await scenario.OpenAsync();
        await scenario.UpdateAgentAsync(row => row.Operations = [AgentOperation.Read, AgentOperation.Update]);

        var revised = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithSummary, ChangeSetTarget.Existing(own.Id)));

        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Assert.Single(revised.Errors).Code);
        Assert.Empty(await scenario.OperationRowsAsync(own.Id));
        Assert.Equal(1, (await scenario.RunSchemaAsync(directory => directory.GetAsync("product"))).Value.Version);
    }

    // Inside a caller's unit of work the revision's rows are saved, so rolling that unit back is what undoes them.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_rollback_after_the_rows_reached_the_database_leaves_neither_the_version_nor_the_operation(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var changeSet = await scenario.OpenAsync();
        var target = ChangeSetTarget.Existing(changeSet.Id);
        var created = (await scenario.RunSchemaAsync(directory => directory.CreateAsync(Product, target))).Value;
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            Assert.True((await scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>().ReviseAsync("product", 1, WithSummary, target)).IsSuccess);

            Assert.True(await context.ContentTypeVersions.AsNoTracking().AnyAsync(row => row.ContentTypeId == created.Id && row.Version == 2));
            Assert.Equal(2, await context.ChangeSetOperations.AsNoTracking().Where(row => row.ChangeSetId == changeSet.Id).Select(row => row.ProposedVersion).SingleAsync());
        }

        Assert.Equal(1, (await scenario.RunSchemaAsync(directory => directory.GetAsync("product"))).Value.Version);
        Assert.Single(await scenario.QueryAsync(context => context.ContentTypeVersions.AsNoTracking().Where(row => row.ContentTypeId == created.Id).ToListAsync()));
        Assert.Equal(1, Assert.Single(await scenario.OperationRowsAsync(changeSet.Id)).ProposedVersion);
        Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);
    }

    // An open set may still publish the latest draft, so a revision from the published version keeps what the draft adds; once the
    // draft's set is rejected, the draft no longer counts.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_from_the_published_version_keeps_the_entity_flag_of_a_draft_an_open_set_proposes(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var name = new FieldDefinition("name", "text");
        await PublishedProductAsync(scenario);
        var draft = await scenario.OpenAsync();
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, new ContentTypeDefinition([Title, name], isEntity: true), ChangeSetTarget.Existing(draft.Id)));
        var other = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);

        var plain = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, new ContentTypeDefinition([Title, name]), other));

        Assert.Equal(SchemaErrorCodes.EntityFlagRemoved, Assert.Single(plain.Errors).Code);

        await scenario.RunAsync(directory => directory.TransitionAsync(draft.Id, ChangeSetState.ChecksRunning));
        await scenario.RunAsync(directory => directory.TransitionAsync(draft.Id, ChangeSetState.AwaitingReview));
        await scenario.RunAsync(directory => directory.RejectAsync(draft.Id, "Products are not facts."));
        Assert.True((await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, new ContentTypeDefinition([Title, name]), other))).IsSuccess);
    }

    // References are checked against what is live or an open set may still publish: the live version keeps its references after a
    // rejected draft drops them, and a field only a rejected draft has is no target (SCH-04).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task References_follow_the_live_and_open_versions_and_not_rejected_drafts(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var name = new FieldDefinition("name", "text");
        var country = new FieldDefinition("country", "text");
        var brandCountry = new FieldDefinition("brandCountry", "reference", settings: Json("""{"target": "brand", "field": "country"}"""));
        var brandLogo = new FieldDefinition("brandLogo", "reference", settings: Json("""{"target": "brand", "field": "logo"}"""));
        var live = await scenario.OpenAsync();
        await scenario.RunSchemaAsync(directory => directory.CreateAsync(new NewContentType("Brand", "brand", new ContentTypeDefinition([name, country], isEntity: true)), ChangeSetTarget.Existing(live.Id)));
        await scenario.RunSchemaAsync(directory => directory.CreateAsync(new NewContentType("Page", "page", new ContentTypeDefinition([Title, brandCountry])), ChangeSetTarget.Existing(live.Id)));
        await scenario.PublishAsync(live.Id);

        var rejected = await scenario.OpenAsync();
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("page", 1, new ContentTypeDefinition([Title]), ChangeSetTarget.Existing(rejected.Id)));
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("brand", 1, new ContentTypeDefinition([name, country, new FieldDefinition("logo", "text")], isEntity: true), ChangeSetTarget.Existing(rejected.Id)));
        await scenario.RunAsync(directory => directory.TransitionAsync(rejected.Id, ChangeSetState.ChecksRunning));
        await scenario.RunAsync(directory => directory.TransitionAsync(rejected.Id, ChangeSetState.AwaitingReview));
        await scenario.RunAsync(directory => directory.RejectAsync(rejected.Id, "Not now."));
        var next = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);

        var dropCountry = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("brand", 1, new ContentTypeDefinition([name], isEntity: true), next));
        var toRejectedField = await scenario.RunSchemaAsync(directory => directory.CreateAsync(new NewContentType("Ad", "ad", new ContentTypeDefinition([Title, brandLogo])), next));

        Assert.Equal(SchemaErrorCodes.FieldReferenced, Assert.Single(dropCountry.Errors).Code);
        Assert.Contains("'brandCountry' of 'page'", dropCountry.Errors[0].Message);
        Assert.Equal(SchemaErrorCodes.ReferenceFieldUnknown, Assert.Single(toRejectedField.Errors).Code);
    }

    // A rejected create leaves a type no set has published, and a reverted one takes it offline; either way the next revision
    // records a create, so the type can be published again.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_type_whose_create_was_rejected_or_reverted_is_published_again_through_a_create(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);

        var rejected = await scenario.OpenAsync();
        var product = (await scenario.RunSchemaAsync(directory => directory.CreateAsync(Product, ChangeSetTarget.Existing(rejected.Id)))).Value;
        await scenario.RunAsync(directory => directory.TransitionAsync(rejected.Id, ChangeSetState.ChecksRunning));
        await scenario.RunAsync(directory => directory.TransitionAsync(rejected.Id, ChangeSetState.AwaitingReview));
        await scenario.RunAsync(directory => directory.RejectAsync(rejected.Id, "Not this quarter."));

        var retry = await scenario.OpenAsync();
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithSummary, ChangeSetTarget.Existing(retry.Id)));
        Assert.Equal([ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, product.Id, null, 2).Value], (await scenario.RunAsync(directory => directory.GetAsync(retry.Id))).Value.Operations);
        await scenario.PublishAsync(retry.Id);
        Assert.Equal(2, (await scenario.RunSchemaAsync(directory => directory.GetPublishedAsync("product"))).Value.Version);

        var published = await scenario.OpenAsync();
        var category = (await scenario.RunSchemaAsync(directory => directory.CreateAsync(new NewContentType("Category", "category", new ContentTypeDefinition([Title])), ChangeSetTarget.Existing(published.Id)))).Value;
        await scenario.PublishAsync(published.Id);
        var revert = (await scenario.RunAsync(directory => directory.RevertAsync(published.Id, "Categories come later."))).Value;
        await scenario.PublishAsync(revert.Id);
        Assert.Equal(SchemaErrorCodes.ContentTypeNotPublished, Assert.Single((await scenario.RunSchemaAsync(directory => directory.GetPublishedAsync("category"))).Errors).Code);

        var again = await scenario.OpenAsync();
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("category", 1, new ContentTypeDefinition([Title, new FieldDefinition("parent", "text")]), ChangeSetTarget.Existing(again.Id)));
        Assert.Equal([ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, category.Id, null, 2).Value], (await scenario.RunAsync(directory => directory.GetAsync(again.Id))).Value.Operations);
        await scenario.PublishAsync(again.Id);
        Assert.Equal(2, (await scenario.RunSchemaAsync(directory => directory.GetPublishedAsync("category"))).Value.Version);
    }

    // The published version is behind the latest draft, whose fields other drafts may already reference (SCH-04).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_from_the_published_version_cannot_drop_a_field_a_draft_references(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var name = new FieldDefinition("name", "text");
        var first = await scenario.OpenAsync();
        await scenario.RunSchemaAsync(directory => directory.CreateAsync(new NewContentType("Brand", "brand", new ContentTypeDefinition([name], isEntity: true)), ChangeSetTarget.Existing(first.Id)));
        await scenario.PublishAsync(first.Id);

        var draft = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("brand", 1, new ContentTypeDefinition([name, new FieldDefinition("country", "text")], isEntity: true), draft));
        var country = new FieldDefinition("brandCountry", "reference", settings: Json("""{"target": "brand", "field": "country"}"""));
        Assert.True((await scenario.RunSchemaAsync(directory => directory.CreateAsync(new NewContentType("Product", "product", new ContentTypeDefinition([Title, country])), draft))).IsSuccess);

        var other = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);
        var revision = await scenario.RunSchemaAsync(directory => directory.ReviseAsync(
            "brand",
            1,
            new ContentTypeDefinition([name, new FieldDefinition("logo", "text")], isEntity: true),
            other));

        var error = Assert.Single(revision.Errors);
        Assert.Equal(SchemaErrorCodes.FieldReferenced, error.Code);
        Assert.Contains("'country'", error.Message);
        Assert.Equal(2, (await scenario.RunSchemaAsync(directory => directory.GetAsync("brand"))).Value.Version);
    }

    // A draft that removes a field is what its type is heading to, so the field is no target while an open set may still publish
    // that draft, whichever set the reference is written into (SCH-04).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_field_a_pending_draft_removes_cannot_be_referenced(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        await PublishedBrandAndPageAsync(scenario);
        var draft = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("brand", 1, new ContentTypeDefinition([Name], isEntity: true), draft));
        var other = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);

        var sameSet = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("page", 1, new ContentTypeDefinition([Title, BrandLogo]), draft));
        var otherSet = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("page", 1, new ContentTypeDefinition([Title, BrandLogo]), other));

        foreach (var refused in new[] { sameSet, otherSet })
        {
            var error = Assert.Single(refused.Errors);
            Assert.Equal(SchemaErrorCodes.ReferenceFieldUnknown, error.Code);
            Assert.Contains("version 2 of the entity type 'brand'", error.Message);
        }
    }

    // Every draft an open set proposes may still publish, not only the latest one, so a field any of them references stays.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_field_an_older_open_draft_references_cannot_be_removed(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        await PublishedBrandAndPageAsync(scenario);
        var referencing = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("page", 1, new ContentTypeDefinition([Title, BrandLogo]), referencing));
        var latest = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("page", 1, WithSummary, latest));
        var next = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);

        var dropLogo = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("brand", 1, new ContentTypeDefinition([Name], isEntity: true), next));

        var error = Assert.Single(dropLogo.Errors);
        Assert.Equal(SchemaErrorCodes.FieldReferenced, error.Code);
        Assert.Contains("'brandLogo' of 'page'", error.Message);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_keeps_the_entity_flag_of_an_older_open_draft(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        await PublishedProductAsync(scenario);
        var open = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, new ContentTypeDefinition([Title], isEntity: true), open));
        var rejected = await scenario.OpenAsync();
        await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, new ContentTypeDefinition([Title, Name], isEntity: true), ChangeSetTarget.Existing(rejected.Id)));
        await RejectAsync(scenario, rejected.Id);
        var next = ChangeSetTarget.Existing((await scenario.OpenAsync()).Id);

        var plain = await scenario.RunSchemaAsync(directory => directory.ReviseAsync("product", 1, WithPrice, next));

        var error = Assert.Single(plain.Errors);
        Assert.Equal(SchemaErrorCodes.EntityFlagRemoved, error.Code);
        Assert.StartsWith("Version 2 of the content type 'product'", error.Message);
    }

    // The daily cap is counted inside the write's unit of work, so concurrent opens take turns; a refusal stores nothing but its
    // security event (SEC-08).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_write_that_would_open_a_set_past_the_agents_daily_cap_is_refused(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        await scenario.UpdateAgentAsync(row => row.ChangeSetsPerDay = 1);
        await scenario.OpenAsync();

        var refused = await scenario.RunSchemaAsync(directory => directory.CreateAsync(Product, ChangeSetTarget.Session("schema-session", CatalogSync)));

        Assert.Equal(AgentPermissionErrorCodes.CapExceeded, Assert.Single(refused.Errors).Code);
        Assert.Single(await scenario.ChangeSetRowsAsync());
        Assert.False(await scenario.QueryAsync(context => context.ContentTypes.AnyAsync(row => row.Slug == "product")));
        Assert.Single(await scenario.QueryAsync(context => context.AuditEvents
            .AsNoTracking()
            .Where(row => row.Operation == "agent.change_sets_per_day_exceeded")
            .ToListAsync()));
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

    // The content type and change set events of writes, in the order they were appended; security events are left out.
    private static async Task<List<string>> WriteEventsAsync(ChangeSetScenario scenario)
    {
        var rows = await scenario.QueryAsync(context => context.AuditEvents
            .AsNoTracking()
            .Where(row => row.Operation.StartsWith("content_type.") || row.Operation.StartsWith("change_set."))
            .ToListAsync());

        return [.. rows.OrderBy(row => row.Timestamp).Select(row => row.Operation)];
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
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

    // An entity brand with a logo, and a page that references nothing yet, both live.
    private static async Task PublishedBrandAndPageAsync(ChangeSetScenario scenario)
    {
        var changeSet = await scenario.OpenAsync();
        var target = ChangeSetTarget.Existing(changeSet.Id);
        await scenario.RunSchemaAsync(directory => directory.CreateAsync(new NewContentType("Brand", "brand", new ContentTypeDefinition([Name, Logo], isEntity: true)), target));
        await scenario.RunSchemaAsync(directory => directory.CreateAsync(new NewContentType("Page", "page", new ContentTypeDefinition([Title])), target));
        await scenario.PublishAsync(changeSet.Id);
    }

    private static async Task RejectAsync(ChangeSetScenario scenario, Guid changeSetId)
    {
        await scenario.RunAsync(directory => directory.TransitionAsync(changeSetId, ChangeSetState.ChecksRunning));
        await scenario.RunAsync(directory => directory.TransitionAsync(changeSetId, ChangeSetState.AwaitingReview));
        await scenario.RunAsync(directory => directory.RejectAsync(changeSetId, "Not now."));
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
