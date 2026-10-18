using System.Text.Json;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Schema.SchemaScenario;

namespace FluentCMS.IntegrationTests.Schema;

public sealed class ContentTypeDirectoryTests
{
    private static readonly FieldDefinition[] ProductFields =
    [
        Field("name", "text", """{"maxLength": 200}""", required: true),
        Field("price", "number", """{"min": 0, "max": 99999.99}""", required: true),
        Field("tags", "list", """{"itemType": "text"}"""),
    ];

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_created_type_belongs_to_the_one_site_starts_at_version_one_and_is_audited(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);

        var created = (await scenario.CreateAsync("product", ProductFields)).Value;
        var read = (await scenario.RunAsync(directory => directory.GetAsync("product"))).Value;

        Assert.Equal(Site.Default.Id, created.SiteId);
        Assert.Equal(1, created.Version);
        Assert.Equal(created.Id, read.Id);
        Assert.Equal(Site.Default.Id, read.SiteId);
        Assert.Equal("Product", read.Name);
        Assert.Equal("product", read.Slug);
        Assert.Equal(1, read.Version);
        AssertSameDefinition(created.Definition, read.Definition);

        Assert.Equal(
            [new ContentTypeSummary(created.Id, "Product", "product", 1)],
            await scenario.RunAsync(directory => directory.ListAsync()));
        Assert.Equal(Site.Default.Id, Assert.Single(await scenario.ContentTypeRowsAsync()).SiteId);
        Assert.Equal(
            [Site.Default.Id],
            await scenario.QueryAsync(context => context.Sites.Select(site => site.Id).ToListAsync()));

        var auditEvent = Assert.Single(await scenario.AuditRowsAsync());
        Assert.Equal("content_type.created", auditEvent.Operation);
        Assert.Equal("content_type", auditEvent.SubjectType);
        Assert.Equal(created.Id, auditEvent.SubjectId);
        Assert.Equal(Amir.UserId, auditEvent.InitiatorId);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_stores_the_next_version_and_the_earlier_one_stays_readable(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        var original = (await scenario.CreateAsync("product", ProductFields)).Value;
        var revisedDefinition = new ContentTypeDefinition([.. ProductFields, Field("releasedOn", "dateTime", """{"dateOnly": true}""")]);

        var revised = (await scenario.RunAsync(directory => directory.ReviseAsync("product", 1, revisedDefinition))).Value;

        Assert.Equal(2, revised.Version);
        Assert.Equal(original.Id, revised.Id);
        var current = (await scenario.RunAsync(directory => directory.GetAsync("product"))).Value;
        var first = (await scenario.RunAsync(directory => directory.GetVersionAsync("product", 1))).Value;
        Assert.Equal(2, current.Version);
        AssertSameDefinition(revisedDefinition, current.Definition);
        Assert.Equal(1, first.Version);
        AssertSameDefinition(original.Definition, first.Definition);
        Assert.Equal(2, Assert.Single(await scenario.ContentTypeRowsAsync()).Version);

        var missing = await scenario.RunAsync(directory => directory.GetVersionAsync("product", 3));
        Assert.Equal(SchemaErrorCodes.VersionNotFound, Assert.Single(missing.Errors).Code);

        var versions = await scenario.VersionRowsAsync();
        var revision = Assert.Single(await scenario.AuditRowsAsync(), row => row.Operation == "content_type.revised");
        Assert.Equal([1, 2], versions.Select(version => version.Version));
        Assert.Equal(original.Id, revision.SubjectId);
        Assert.Equal(versions[0].Id, revision.PreviousVersionId);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_slug_is_unique_within_the_site(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        await scenario.CreateAsync("product", ProductFields);

        var second = await scenario.CreateAsync("product", Field("title", "text"));

        Assert.Equal(SchemaErrorCodes.SlugTaken, Assert.Single(second.Errors).Code);
        await AssertStoredAsync(scenario, types: 1, versions: 1, events: 1);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_reference_targets_a_stored_type_of_the_site_or_the_type_itself(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);

        var tooEarly = await scenario.CreateAsync("product-page", Reference("product", "product"), References("related", "category"));

        Assert.Equal(
            [
                "Field 'product': there is no content type 'product' in this site to reference.",
                "Field 'related': there is no content type 'category' in this site to reference.",
            ],
            tooEarly.Errors.Select(error => error.Message));
        Assert.All(tooEarly.Errors, error => Assert.Equal(SchemaErrorCodes.ReferenceTargetUnknown, error.Code));
        await AssertStoredAsync(scenario, types: 0, versions: 0, events: 0);

        Assert.True((await scenario.CreateAsync("category", Field("name", "text"), Reference("parent", "category"))).IsSuccess);
        Assert.True((await scenario.CreateAsync("product", ProductFields)).IsSuccess);
        Assert.True((await scenario.CreateAsync("product-page", Reference("product", "product"), References("related", "category"))).IsSuccess);

        var revision = await scenario.RunAsync(directory => directory.ReviseAsync(
            "category",
            1,
            new ContentTypeDefinition([Field("name", "text"), References("regions", "region")])));

        Assert.Equal(SchemaErrorCodes.ReferenceTargetUnknown, Assert.Single(revision.Errors).Code);
        await AssertStoredAsync(scenario, types: 3, versions: 3, events: 3);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_starts_from_the_current_version(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        await scenario.CreateAsync("product", ProductFields);
        var definition = new ContentTypeDefinition([.. ProductFields, Field("summary", "text")]);

        var first = await scenario.RunAsync(directory => directory.ReviseAsync("product", 1, definition));
        var stale = await scenario.RunAsync(directory => directory.ReviseAsync("product", 1, definition));
        var ahead = await scenario.RunAsync(directory => directory.ReviseAsync("product", 5, definition));

        Assert.Equal(2, first.Value.Version);
        Assert.Equal(SchemaErrorCodes.VersionConflict, Assert.Single(stale.Errors).Code);
        Assert.Equal(SchemaErrorCodes.VersionConflict, Assert.Single(ahead.Errors).Code);
        await AssertStoredAsync(scenario, types: 1, versions: 2, events: 2);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Invalid_definitions_and_unknown_types_fail_without_writing(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);

        var invalid = await scenario.CreateAsync("product", Field("price", "money"), Field("price", "number"));
        var unknownRead = await scenario.RunAsync(directory => directory.GetAsync("product"));
        var unknownRevision = await scenario.RunAsync(directory => directory.ReviseAsync("product", 1, new ContentTypeDefinition([])));

        Assert.Equal([SchemaErrorCodes.FieldTypeUnknown, SchemaErrorCodes.FieldNameDuplicate], invalid.Errors.Select(error => error.Code));
        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, Assert.Single(unknownRead.Errors).Code);
        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, Assert.Single(unknownRevision.Errors).Code);
        Assert.Empty(await scenario.RunAsync(directory => directory.ListAsync()));

        await scenario.CreateAsync("product", ProductFields);
        var invalidRevision = await scenario.RunAsync(directory => directory.ReviseAsync("product", 1, new ContentTypeDefinition([Field("Name", "text")])));

        Assert.Equal(SchemaErrorCodes.FieldNameInvalid, Assert.Single(invalidRevision.Errors).Code);
        await AssertStoredAsync(scenario, types: 1, versions: 1, events: 1);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_create_in_an_abandoned_unit_of_work_leaves_neither_the_type_nor_its_event(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            await using (await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync())
            {
                var created = await scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>()
                    .CreateAsync(new NewContentType("Product", "product", new ContentTypeDefinition(ProductFields)));
                Assert.True(created.IsSuccess);
            }
        }

        await AssertStoredAsync(scenario, types: 0, versions: 0, events: 0);
    }

    // Expected failures are found before the directory opens its own unit of work, so they never doom the caller's.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failed_create_leaves_the_callers_unit_of_work_able_to_commit(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            Assert.True((await directory.CreateAsync(new NewContentType("Product", "product", new ContentTypeDefinition(ProductFields)))).IsSuccess);
            var duplicate = await directory.CreateAsync(new NewContentType("Product again", "product", new ContentTypeDefinition(ProductFields)));
            var dangling = await directory.CreateAsync(new NewContentType("Page", "page", new ContentTypeDefinition([Reference("about", "nothing")])));

            Assert.Equal(SchemaErrorCodes.SlugTaken, Assert.Single(duplicate.Errors).Code);
            Assert.Equal(SchemaErrorCodes.ReferenceTargetUnknown, Assert.Single(dangling.Errors).Code);
            await work.CommitAsync();
        }

        await AssertStoredAsync(scenario, types: 1, versions: 1, events: 1);
    }

    // The second writer checks while the first has not committed, so its check passes and the unique index has the last word.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_two_concurrent_creates_of_one_slug_one_wins_and_the_other_gets_slug_taken(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        var product = new NewContentType("Product", "product", new ContentTypeDefinition(ProductFields));

        var lost = await RaceAsync(scenario, directory => directory.CreateAsync(product), directory => directory.CreateAsync(product));

        Assert.Equal(SchemaErrorCodes.SlugTaken, Assert.Single(lost.Errors).Code);
        await AssertStoredAsync(scenario, types: 1, versions: 1, events: 1);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_two_concurrent_revisions_of_one_version_one_wins_and_the_other_gets_a_conflict(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        await scenario.CreateAsync("product", ProductFields);
        var definition = new ContentTypeDefinition([.. ProductFields, Field("summary", "text")]);

        var lost = await RaceAsync(
            scenario,
            directory => directory.ReviseAsync("product", 1, definition),
            directory => directory.ReviseAsync("product", 1, definition));

        Assert.Equal(SchemaErrorCodes.VersionConflict, Assert.Single(lost.Errors).Code);
        Assert.Equal(2, Assert.Single(await scenario.ContentTypeRowsAsync()).Version);
        await AssertStoredAsync(scenario, types: 1, versions: 2, events: 2);
    }

    // The model behind the reference scenario (02-personas-and-scenarios), the setup for S1 and S2 in the F-SCH acceptance.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_reference_scenario_model_can_be_defined(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        (string Slug, bool IsEntity, FieldDefinition[] Fields)[] model =
        [
            ("category", true, [Field("name", "text", required: true), Reference("parent", "category")]),
            ("region", true, [Field("name", "text", required: true), Field("code", "text", """{"maxLength": 8}""")]),
            ("spec", true, [Field("name", "text", required: true), Field("value", "text"), Field("unit", "enum", """{"values": ["mm", "kg", "W"]}""")]),
            ("product", true, [.. ProductFields, Reference("category", "category"), References("specs", "spec"), References("regions", "region"), Field("hero", "media")]),
            ("product-page", false, [Reference("product", "product"), Reference("price", "product", "price"), Field("headline", "text"), Field("body", "richText")]),
            ("category-page", false, [Reference("category", "category"), Field("intro", "richText")]),
            ("comparison-page", false, [References("products", "product"), References("prices", "product", "price"), Field("verdict", "richText"), Field("extra", "json")]),
            ("blog-post", false, [Field("title", "text", required: true), Field("body", "richText"), Field("publishedOn", "dateTime"), Field("featured", "boolean"), References("related", "product")]),
        ];

        foreach (var (slug, isEntity, fields) in model)
        {
            var created = isEntity ? await scenario.CreateEntityAsync(slug, fields) : await scenario.CreateAsync(slug, fields);
            Assert.True(created.IsSuccess, $"{slug}: {string.Join("; ", created.Errors.Select(error => error.Message))}");
        }

        Assert.Equal(
            model.Select(type => type.Slug).Order(StringComparer.Ordinal),
            (await scenario.RunAsync(directory => directory.ListAsync())).Select(summary => summary.Slug));

        foreach (var (slug, isEntity, _) in model)
        {
            Assert.Equal(isEntity, (await scenario.RunAsync(directory => directory.GetAsync(slug))).Value.Definition.IsEntity);
        }

        var productPage = (await scenario.RunAsync(directory => directory.GetAsync("product-page"))).Value;
        Assert.Contains(
            new FieldReference("price", new ReferenceTarget("product", "price")),
            productPage.Definition.References(scenario.Services.GetRequiredService<FieldTypeRegistry>()));
    }

    [PostgresFact]
    public async Task Definitions_are_stored_as_jsonb_on_postgresql()
    {
        await using var scenario = await SchemaScenario.CreateAsync(DatabaseProviders.PostgreSql);

        var type = await scenario.QueryAsync(context => context.Database
            .SqlQueryRaw<string>(
                "SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_name = 'ContentTypeVersions' AND column_name = 'Definition'")
            .SingleAsync());

        Assert.Equal("jsonb", type);
    }

    private static async Task<Result<ContentType>> RaceAsync(
        SchemaScenario scenario,
        Func<IContentTypeDirectory, Task<Result<ContentType>>> winner,
        Func<IContentTypeDirectory, Task<Result<ContentType>>> loser)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

        Assert.True((await winner(scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>())).IsSuccess);

        var second = Task.Run(() => scenario.RunAsync(loser));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await work.CommitAsync();
        await work.DisposeAsync();

        return await second;
    }

    private static async Task AssertStoredAsync(SchemaScenario scenario, int types, int versions, int events)
    {
        Assert.Equal(types, (await scenario.ContentTypeRowsAsync()).Count);
        Assert.Equal(versions, (await scenario.VersionRowsAsync()).Count);
        Assert.Equal(events, (await scenario.AuditRowsAsync()).Count);
    }

    // PostgreSQL's jsonb keeps the meaning of a document, not its text, so definitions are compared as JSON.
    private static void AssertSameDefinition(ContentTypeDefinition expected, ContentTypeDefinition actual)
    {
        Assert.True(
            JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(expected.ToJson()), JsonSerializer.Deserialize<JsonElement>(actual.ToJson())),
            $"Expected {expected.ToJson()} but read {actual.ToJson()}");
    }
}
