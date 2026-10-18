using FluentCMS.Core.Content;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Content.EntryScenario;
using static FluentCMS.IntegrationTests.Schema.SchemaScenario;

namespace FluentCMS.IntegrationTests.Content;

public sealed class EntryDirectoryTests
{
    private static readonly FieldDefinition[] PostFields =
    [
        Field("title", "text", """{"maxLength": 80}""", required: true),
        Field("body", "richText"),
        Field("publishedOn", "dateTime", """{"dateOnly": true}"""),
    ];

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_created_entry_belongs_to_the_site_starts_at_version_one_and_is_audited(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        var type = (await scenario.Schema.CreateAsync("blog-post", PostFields)).Value;

        var created = (await scenario.CreateAsync("blog-post", """{"title": "Hello", "body": null, "publishedOn": "2026-10-18"}""")).Value;
        var read = (await scenario.RunAsync(directory => directory.GetAsync(created.Id))).Value;

        Assert.Equal(Site.Default.Id, read.SiteId);
        Assert.Equal(type.Id, read.ContentTypeId);
        Assert.Equal("blog-post", read.ContentType);
        Assert.Equal(1, read.LatestVersion);
        Assert.Null(read.PublishedVersion);
        Assert.Equal(1, read.Latest.ContentTypeVersion);
        AssertSameFields("""{"title": "Hello", "publishedOn": "2026-10-18"}""", read.Latest.Fields);

        var row = Assert.Single(await scenario.EntryRowsAsync());
        Assert.Equal(Site.Default.Id, row.SiteId);
        Assert.Equal(1, row.LatestVersion);
        Assert.Null(row.PublishedVersion);

        var auditEvent = Assert.Single(await scenario.EntryEventsAsync());
        Assert.Equal("entry.created", auditEvent.Operation);
        Assert.Equal(created.Id, auditEvent.SubjectId);
        Assert.Equal(Amir.UserId, auditEvent.InitiatorId);
        Assert.Null(auditEvent.PreviousVersionId);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Every_update_stores_the_next_version_and_the_history_lists_them_all(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateAsync("blog-post", PostFields);
        var id = (await scenario.CreateAsync("blog-post", """{"title": "First"}""")).Value.Id;

        var second = (await scenario.UpdateAsync(id, 1, """{"title": "Second"}""")).Value;
        var third = (await scenario.UpdateAsync(id, 2, """{"title": "Third", "body": "Now with a body."}""")).Value;

        Assert.Equal(2, second.LatestVersion);
        Assert.Equal(3, third.LatestVersion);
        Assert.Equal(id, third.Id);

        var latest = (await scenario.RunAsync(directory => directory.GetAsync(id))).Value;
        var first = (await scenario.RunAsync(directory => directory.GetVersionAsync(id, 1))).Value;
        Assert.Equal(3, latest.LatestVersion);
        AssertSameFields("""{"title": "Third", "body": "Now with a body."}""", latest.Latest.Fields);
        AssertSameFields("""{"title": "First"}""", first.Fields);

        var history = (await scenario.RunAsync(directory => directory.HistoryAsync(id))).Value;
        Assert.Equal([1, 2, 3], history.Select(version => version.Version));
        Assert.All(history, version => Assert.Equal(1, version.ContentTypeVersion));

        var missing = await scenario.RunAsync(directory => directory.GetVersionAsync(id, 4));
        Assert.Equal(ContentErrorCodes.VersionNotFound, Assert.Single(missing.Errors).Code);

        var versions = await scenario.VersionRowsAsync();
        var updates = (await scenario.EntryEventsAsync()).Where(row => row.Operation == "entry.updated").ToList();
        Assert.Equal(2, updates.Count);
        Assert.All(updates, update => Assert.Equal(id, update.SubjectId));
        Assert.Equal(new[] { versions[0].Id, versions[1].Id }.Order(), updates.Select(update => update.PreviousVersionId!.Value).Order());
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_update_from_a_version_other_than_the_latest_gets_a_conflict_and_writes_nothing(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateAsync("blog-post", PostFields);
        var id = (await scenario.CreateAsync("blog-post", """{"title": "First"}""")).Value.Id;
        await scenario.UpdateAsync(id, 1, """{"title": "Second"}""");

        var stale = await scenario.UpdateAsync(id, 1, """{"title": "Stale"}""");
        var ahead = await scenario.UpdateAsync(id, 5, """{"title": "Ahead"}""");

        Assert.Equal(ContentErrorCodes.VersionConflict, Assert.Single(stale.Errors).Code);
        Assert.Equal(ContentErrorCodes.VersionConflict, Assert.Single(ahead.Errors).Code);
        await scenario.AssertStoredAsync(entries: 1, versions: 2, events: 2);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Invalid_fields_and_unknown_types_entries_and_versions_fail_without_writing(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateAsync("blog-post", PostFields);

        var invalid = await scenario.CreateAsync("blog-post", """{"titel": "Typo", "publishedOn": "yesterday"}""");
        var unknownType = await scenario.CreateAsync("news", """{"title": "Hello"}""");
        var unknownEntry = Guid.CreateVersion7();
        var unknownRead = await scenario.RunAsync(directory => directory.GetAsync(unknownEntry));
        var unknownVersion = await scenario.RunAsync(directory => directory.GetVersionAsync(unknownEntry, 1));
        var unknownHistory = await scenario.RunAsync(directory => directory.HistoryAsync(unknownEntry));
        var unknownUpdate = await scenario.UpdateAsync(unknownEntry, 1, """{"title": "Hello"}""");

        Assert.Equal(
            [ContentErrorCodes.FieldUnknown, ContentErrorCodes.FieldRequired, SchemaErrorCodes.ValueInvalid],
            invalid.Errors.Select(error => error.Code));
        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, Assert.Single(unknownType.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single(unknownRead.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single(unknownVersion.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single(unknownHistory.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single(unknownUpdate.Errors).Code);
        await scenario.AssertStoredAsync(entries: 0, versions: 0, events: 0);

        var id = (await scenario.CreateAsync("blog-post", """{"title": "Hello"}""")).Value.Id;
        var invalidUpdate = await scenario.UpdateAsync(id, 1, """{"title": 42}""");

        Assert.Equal("Field 'title': The value must be a string.", Assert.Single(invalidUpdate.Errors).Message);
        await scenario.AssertStoredAsync(entries: 1, versions: 1, events: 1);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Updates_are_checked_against_the_current_version_of_the_type(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateAsync("blog-post", PostFields);
        var id = (await scenario.CreateAsync("blog-post", """{"title": "Hello", "body": "Text"}""")).Value.Id;
        await scenario.Schema.ReviseAsync("blog-post", 1, new ContentTypeDefinition([PostFields[0], Field("summary", "text", required: true)]));

        var stale = await scenario.UpdateAsync(id, 1, """{"title": "Hello", "body": "Text"}""");
        var current = (await scenario.UpdateAsync(id, 1, """{"title": "Hello", "summary": "Short"}""")).Value;

        Assert.Equal([ContentErrorCodes.FieldUnknown, ContentErrorCodes.FieldRequired], stale.Errors.Select(error => error.Code));
        Assert.Equal(2, current.Latest.ContentTypeVersion);
        Assert.Equal([1, 2], (await scenario.RunAsync(directory => directory.HistoryAsync(id))).Value.Select(version => version.ContentTypeVersion));

        // The first version still reads as it was written, although the type no longer has its field.
        AssertSameFields("""{"title": "Hello", "body": "Text"}""", (await scenario.RunAsync(directory => directory.GetVersionAsync(id, 1))).Value.Fields);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Entries_are_listed_per_content_type(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateAsync("blog-post", PostFields);
        await scenario.Schema.CreateAsync("note", Field("text", "text"));
        var first = (await scenario.CreateAsync("blog-post", """{"title": "One"}""")).Value;
        await scenario.CreateAsync("note", """{"text": "Elsewhere"}""");
        var second = (await scenario.CreateAsync("blog-post", """{"title": "Two"}""")).Value;
        await scenario.UpdateAsync(second.Id, 1, """{"title": "Two, again"}""");

        var posts = (await scenario.RunAsync(directory => directory.ListAsync("blog-post"))).Value;
        var unknown = await scenario.RunAsync(directory => directory.ListAsync("news"));

        // Ids are version 7, so their order is the order of creation to the millisecond.
        Assert.Equal(new[] { new EntrySummary(first.Id, 1, null), new EntrySummary(second.Id, 2, null) }.OrderBy(entry => entry.Id), posts);
        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, Assert.Single(unknown.Errors).Code);
    }

    // CNT-02: whatever the caller, a stored version cannot change.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_stored_version_cannot_be_overwritten(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateAsync("blog-post", PostFields);
        await scenario.CreateAsync("blog-post", """{"title": "Original"}""");

        await scenario.Schema.QueryAsync(async context =>
        {
            await Assert.ThrowsAnyAsync<Exception>(() => context.EntryVersions.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Fields, """{"title": "Tampered"}""")));
            await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("UPDATE \"EntryVersions\" SET \"Version\" = 2"));

            var tracked = await context.EntryVersions.SingleAsync();
            tracked.ContentTypeVersion = 2;
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            return 0;
        });

        var stored = Assert.Single(await scenario.VersionRowsAsync());
        Assert.Equal(1, stored.Version);
        Assert.Equal(1, stored.ContentTypeVersion);
        AssertSameFields("""{"title": "Original"}""", Json(stored.Fields));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_create_in_an_abandoned_unit_of_work_leaves_neither_the_entry_nor_its_event(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateAsync("blog-post", PostFields);

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            await using (await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync())
            {
                var created = await scope.ServiceProvider.GetRequiredService<IEntryDirectory>()
                    .CreateAsync(new NewEntry("blog-post", Json("""{"title": "Hello"}""")));
                Assert.True(created.IsSuccess);
            }
        }

        await scenario.AssertStoredAsync(entries: 0, versions: 0, events: 0);
    }

    // Expected failures are found before the directory opens its own unit of work, so they never doom the caller's.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failed_write_leaves_the_callers_unit_of_work_able_to_commit(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateAsync("blog-post", PostFields);

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<IEntryDirectory>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            var created = (await directory.CreateAsync(new NewEntry("blog-post", Json("""{"title": "Hello"}""")))).Value;
            var invalid = await directory.CreateAsync(new NewEntry("blog-post", Json("{}")));
            var stale = await directory.UpdateAsync(created.Id, 2, Json("""{"title": "Stale"}"""));
            var unknown = await directory.CreateAsync(new NewEntry("news", Json("{}")));

            Assert.False(invalid.IsSuccess);
            Assert.Equal(ContentErrorCodes.VersionConflict, Assert.Single(stale.Errors).Code);
            Assert.False(unknown.IsSuccess);
            await work.CommitAsync();
        }

        await scenario.AssertStoredAsync(entries: 1, versions: 1, events: 1);
    }

    // The second writer reads while the first has not committed, so its check passes and the pointer update has the last word.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_two_concurrent_updates_from_one_version_one_wins_and_the_other_gets_a_conflict(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateAsync("blog-post", PostFields);
        var id = (await scenario.CreateAsync("blog-post", """{"title": "First"}""")).Value.Id;

        Result<Entry> lost;
        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            Assert.True((await scope.ServiceProvider.GetRequiredService<IEntryDirectory>().UpdateAsync(id, 1, Json("""{"title": "Winner"}"""))).IsSuccess);

            var second = Task.Run(() => scenario.UpdateAsync(id, 1, """{"title": "Loser"}"""));
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await work.CommitAsync();
            await work.DisposeAsync();
            lost = await second;
        }

        Assert.Equal(ContentErrorCodes.VersionConflict, Assert.Single(lost.Errors).Code);
        AssertSameFields("""{"title": "Winner"}""", (await scenario.RunAsync(directory => directory.GetAsync(id))).Value.Latest.Fields);
        await scenario.AssertStoredAsync(entries: 1, versions: 2, events: 2);
    }

    // S2 in the F-CNT acceptance, as far as storage goes: the page stores the product, not a copy of its price.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_page_that_shows_a_products_price_stores_the_product_and_no_price(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateReferenceModelAsync();

        var product = await scenario.CreateAsync("product", """
            {
              "name": {"value": "Widget", "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null},
              "price": {
                "value": 19.99,
                "source": {"kind": "url", "reference": "https://example.com/prices"},
                "owner": null,
                "validFrom": "2026-01-01T00:00:00Z",
                "validUntil": null,
                "lastVerified": "2026-10-01T08:00:00Z"
              }
            }
            """);
        Assert.True(product.IsSuccess, string.Join("; ", product.Errors.Select(error => error.Message)));

        var page = (await scenario.CreateAsync("product-page", $$"""
            {"product": "{{product.Value.Id}}", "price": "{{product.Value.Id}}", "headline": "The Widget"}
            """)).Value;
        var literal = await scenario.CreateAsync("product-page", """{"price": 19.99, "headline": "The Widget"}""");

        AssertSameFields(
            $$"""{"product": "{{product.Value.Id}}", "price": "{{product.Value.Id}}", "headline": "The Widget"}""",
            (await scenario.RunAsync(directory => directory.GetAsync(page.Id))).Value.Latest.Fields);
        Assert.Equal(
            $"Field 'price': The value must be the id of a 'product' entity, a UUID such as {FieldValueDescription.ExampleId}.",
            Assert.Single(literal.Errors).Message);
    }

    [PostgresFact]
    public async Task Fields_are_stored_as_jsonb_on_postgresql()
    {
        await using var scenario = await EntryScenario.CreateAsync(DatabaseProviders.PostgreSql);

        var type = await scenario.Schema.QueryAsync(context => context.Database
            .SqlQueryRaw<string>(
                "SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_name = 'EntryVersions' AND column_name = 'Fields'")
            .SingleAsync());

        Assert.Equal("jsonb", type);
    }
}
