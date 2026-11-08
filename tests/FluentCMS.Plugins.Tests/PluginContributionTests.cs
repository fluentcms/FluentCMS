using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Plugins.Sample;
using FluentCMS.Schema;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Tests;

// The contributions of #2829, through the sample plugin that every host of these tests discovers.
public sealed class PluginContributionTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public void TheSamplePluginReferencesTheContractPackagesAndNotCore()
    {
        var references = typeof(SamplePlugin).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .Where(name => name.StartsWith("FluentCMS.", StringComparison.Ordinal))
            .ToList();

        references.ShouldBe(["FluentCMS.Sdk", "FluentCMS.Plugins.Abstractions"], ignoreOrder: true);
    }

    [Fact]
    public async Task TheSamplePluginBootsWithItsTablePermissionSettingsAndJob()
    {
        await using var host = new FluentCmsTestHost();

        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>();
        (await database.Database.GetAppliedMigrationsAsync(Token)).ShouldBe(["20261107014749_InitialSample"]);
        (await database.Set<SampleNote>().CountAsync(Token)).ShouldBe(0);
        (await TablesAsync(database)).ShouldContain("cms_sample__migrations");

        var catalogue = host.Services.GetRequiredService<PermissionCatalogue>();
        catalogue.Find(SamplePermissions.NotesManage)!.Source.ShouldBe(PermissionSource.Plugin);
        catalogue.Contains("settings.sample.manage").ShouldBeTrue();
        (await scope.ServiceProvider.GetRequiredService<ISettings<SampleSettings>>().GetDefaultAsync(Token)).Greeting.ShouldBe("hello");
        host.Services.GetServices<JobRegistration>().ShouldContain(job => job.Type == SampleNoteJob.Type);
        host.Services.GetRequiredService<JobRunnerOptions>().Queues[SampleNoteJob.Queue].ShouldBe(1);
    }

    [Fact]
    public async Task TheHostsWorkersForAPluginsQueueWin()
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.Jobs.Options.Queues[SampleNoteJob.Queue] = 0);

        host.Services.GetRequiredService<JobRunnerOptions>().Queues[SampleNoteJob.Queue].ShouldBe(0);
    }

    [Fact]
    public async Task ThePluginsTablesJoinTheRuntimeModelOutsideItsMigrations()
    {
        await using var host = new FluentCmsTestHost();

        await using var scope = host.Services.CreateAsyncScope();
        var model = scope.ServiceProvider.GetRequiredService<CmsDbContext>().GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(SampleNote)).ShouldNotBeNull();

        entity.GetTableName().ShouldBe(SampleModel.Table);
        entity.IsTableExcludedFromMigrations().ShouldBeTrue();
    }

    [Fact]
    public async Task PluginStreamsRunAfterTheSystemStreamInPluginOrder()
    {
        var file = Path.Combine(Path.GetTempPath(), $"fluentcms-plugins-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddFluentCms(cms => cms.UseSqlite($"Data Source={file};Pooling=False").AddPlugin<LatePlugin>());
            await using var provider = services.BuildServiceProvider();

            var applied = await provider.GetRequiredService<MigrationRunner>().MigrateAsync(Token);

            applied.Select(migration => migration.Stream).Distinct().ShouldBe([MigrationRunner.SystemStream, "sample", "late"]);
            applied.Where(migration => migration.Stream == "late").Select(migration => migration.Id).ShouldBe([LatePlugin.MigrationId]);
            await using var scope = provider.CreateAsyncScope();
            var tables = await TablesAsync(scope.ServiceProvider.GetRequiredService<PluginDbContext<LatePlugin>>());
            tables.ShouldContain("cms_late__migrations");
            tables.ShouldContain(LatePlugin.Table);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(file);
        }
    }

    [Fact]
    public async Task SchemaSyncLeavesThePluginsTableAloneAndRecordsTheSchema()
    {
        await using var host = new FluentCmsTestHost();
        var runner = host.Services.GetRequiredService<MigrationRunner>();

        var report = await runner.SyncContentSchemaAsync(cancellationToken: Token);

        report.NotApplied.ShouldBeEmpty();
        (await runner.GetSchemaStatusAsync(Token)).AppliedHash.ShouldNotBeNull();
    }

    // The sample's set is scaffolded on SQLite, and the late plugin's hand-written migration has no target model to tell.
    [Fact]
    public async Task APostgreSqlHostRefusesThePluginsSqliteSetBeforeTouchingTheDatabase()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddFluentCms(cms => cms.UsePostgreSql("Host=localhost;Database=never_opened").AddPlugin<LatePlugin>());
        await using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<MigrationRunner>();

        var atStartup = await Should.ThrowAsync<FluentCmsConfigurationException>(() => runner.RunAtStartupAsync(Token));
        var migrate = await Should.ThrowAsync<FluentCmsConfigurationException>(() => runner.MigrateAsync(Token));

        atStartup.Code.ShouldBe("FCMS1306");
        atStartup.Message.ShouldBe("FCMS1306: stream sample was scaffolded for SQLite; this host runs PostgreSQL, and a migration set applies only to the provider it was scaffolded for");
        migrate.Message.ShouldBe(atStartup.Message);
    }

    [Fact]
    public void ATableOutsideThePluginsPrefixStopsTheHost()
    {
        Refused(cms => cms.AddPlugin<StrayTablePlugin>(), "FCMS1523", "'stray'", "'cms_other_item'", "cms_stray_");
        Refused(cms => cms.AddPlugin<HistoryTablePlugin>(), "FCMS1523", "'cms_history__migrations'");
        Refused(cms => cms.AddPlugin<AiPlugin>().AddPlugin<AiAssistPlugin>(), "FCMS1523", "'ai-assist'", "'cms_ai_assist_usage'", "the plugin 'ai'");
        Refused(cms => cms.AddPlugin<SiteTablePlugin>(), "FCMS1523", "'cms_site_locale'", "core");
    }

    [Fact]
    public async Task AContentTypeWhoseTableAPluginMapsStopsTheHost()
    {
        await using var host = new FluentCmsTestHost().WithServices(services => services.AddSingleton(provider => new SchemaBuilder(provider.GetRequiredService<IFieldTypeRegistry>())
            .AddCollection("sample-note", note => note.Field("title", "text"))
            .Build()));

        var exception = Should.Throw<FluentCmsConfigurationException>(() => host.Services);

        exception.Code.ShouldBe("FCMS1206");
        exception.Message.ShouldContain("cms_sample_note");
    }

    [Fact]
    public void ARequiredFieldReferenceStopsTheHost() =>
        Refused(cms => cms.AddPlugin<RequiredReferencePlugin>(), "FCMS1526", "'required'", "'of'");

    [Fact]
    public void APluginHasOneMigrationStream() =>
        Refused(cms => cms.AddPlugin<TwiceMigratedPlugin>(), "FCMS1525", "'twice'");

    [Theory]
    [InlineData("FCMS3104", 404, "Not found", "a code of core")]
    [InlineData(SamplePlugin.NoteExists, 409, "Taken", "the plugin 'sample'")]
    [InlineData("fcms0001", 409, "Lower", "upper case letters")]
    [InlineData("FCMS99", 409, "Short", "four digits")]
    [InlineData("FCMS9902", 302, "Redirect", "302")]
    [InlineData("FCMS9902", 409, " ", "no title")]
    public void AnErrorCodeThatCannotBeAPluginsStopsTheHost(string code, int status, string title, string named) =>
        Refused(cms => cms.AddPlugin<CodedPlugin>(coded => (coded.Code, coded.Status, coded.Title) = (code, status, title)), "FCMS1522", "'coded'", named);

    [Fact]
    public void AFieldTypeNameThatCoreOrAnotherPluginHasStopsTheHost()
    {
        Refused(cms => cms.AddPlugin<TextImpostorPlugin>(), "FCMS1521", "'typed'", "'text'", "core");
        Refused(cms => cms.AddPlugin<ExcerptImpostorPlugin>(), "FCMS1521", "'excerpt'", "'sampleExcerpt'", "the plugin 'sample'");
    }

    [Fact]
    public void TheTableContributionsNeedTheBuilderOfAHost() =>
        Should.Throw<InvalidOperationException>(() => new FakeServiceBuilder().AddModel<SampleModel>()).Message.ShouldContain(nameof(FakeServiceBuilder));

    [Fact]
    public async Task TheManagementEndpointIsServedUnderTheManagementApiAndThePathBase()
    {
        await using var host = new FluentCmsTestHost().WithServices(services => services.PostConfigure<ReverseProxyOptions>(proxy => proxy.PathBase = "/cms"));
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var client = host.AsKey(key);

        using var notes = await client.GetAsync(new Uri("/cms/api/management/v1/sample/notes", UriKind.Relative), Token);
        using var outside = await client.GetAsync(new Uri("/api/management/v1/sample/notes", UriKind.Relative), Token);
        using var greeting = await client.GetAsync(new Uri("/cms/sample/greeting", UriKind.Relative), Token);

        notes.StatusCode.ShouldBe(HttpStatusCode.OK, await notes.Content.ReadAsStringAsync(Token));
        JsonNode.Parse(await notes.Content.ReadAsStringAsync(Token))!.AsArray().ShouldBeEmpty();
        outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        greeting.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonNode.Parse(await greeting.Content.ReadAsStringAsync(Token))!["text"]!.GetValue<string>().ShouldBe("discovered");
    }

    [Fact]
    public async Task TheManagementEndpointRefusesACallerWithoutTheManagementScope()
    {
        await using var host = new FluentCmsTestHost();
        var delivery = await host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var notes = new Uri("/api/management/v1/sample/notes", UriKind.Relative);

        using var anonymous = await host.CreateClient().GetAsync(notes, Token);
        using var deliveryKey = await host.AsKey(delivery).GetAsync(notes, Token);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        deliveryKey.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(deliveryKey)).ShouldBe("FCMS3103");
    }

    [Fact]
    public async Task ARefusalThePluginThrowsHasThePluginsCodeAndStatus()
    {
        await using var host = new FluentCmsTestHost();
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var client = host.AsKey(key);
        var notes = new Uri("/api/management/v1/sample/notes", UriKind.Relative);

        using var created = await client.PostAsJsonAsync(notes, new { text = "first" }, Token);
        using var refused = await client.PostAsJsonAsync(notes, new { text = "first" }, Token);

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Token));
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = JsonNode.Parse(await refused.Content.ReadAsStringAsync(Token))!;
        problem["code"]!.GetValue<string>().ShouldBe(SamplePlugin.NoteExists);
        problem["title"]!.GetValue<string>().ShouldBe("Note exists");
        problem["type"]!.GetValue<string>().ShouldBe($"urn:fluentcms:error:{SamplePlugin.NoteExists}");
    }

    [Theory]
    [InlineData("/api/management/v1/routed", false)]
    [InlineData("/API/Delivery/v1", false)]
    [InlineData("/connect/routed", false)]
    [InlineData("/{area}", false)]
    [InlineData("/*rest", false)]
    [InlineData("routed", false)]
    [InlineData("/", true)]
    [InlineData("/routed/", true)]
    public async Task APrefixOutsideThePluginsOwnGroupsStopsTheHost(string prefix, bool management)
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.AddPlugin<RoutedPlugin>(routed => (routed.Prefix, routed.Management) = (prefix, management)));

        var exception = Should.Throw<FluentCmsConfigurationException>(() => host.Services);

        exception.Code.ShouldBe("FCMS1524");
        exception.Message.ShouldContain("'routed'");
    }

    [Fact]
    public async Task ThePermissionsOfAPluginAreListedAndGrantable()
    {
        await using var host = new FluentCmsTestHost();
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var client = host.AsKey(key);

        using var listed = await client.GetAsync(new Uri("/api/management/v1/permissions", UriKind.Relative), Token);
        using var role = await client.PostAsync(
            new Uri("/api/management/v1/roles", UriKind.Relative),
            Json($$"""{ "name": "note-keeper", "grants": [ { "permission": "{{SamplePermissions.NotesManage}}" }, { "permission": "settings.sample.manage" } ] }"""),
            Token);

        var items = JsonNode.Parse(await listed.Content.ReadAsStringAsync(Token))!["items"]!.AsArray();
        var entry = items.Single(item => item!["name"]!.GetValue<string>() == SamplePermissions.NotesManage)!;
        entry["description"]!.GetValue<string>().ShouldBe("Add and remove sample notes.");
        entry["source"]!.GetValue<string>().ShouldBe("plugin");
        items.ShouldContain(item => item!["name"]!.GetValue<string>() == "settings.sample.manage");
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task ThePluginsSettingsAreReadAndWrittenThroughTheirArea()
    {
        await using var host = new FluentCmsTestHost();
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var client = host.AsKey(key);
        var area = new Uri("/api/management/v1/settings/sample", UriKind.Relative);

        using var read = await client.GetAsync(area, Token);
        using var write = new HttpRequestMessage(HttpMethod.Put, area) { Content = Json("""{ "greeting": "hi", "maxNotes": 3 }""") };
        write.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        using var written = await client.SendAsync(write, Token);

        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(Token));
        JsonNode.Parse(await read.Content.ReadAsStringAsync(Token))!["value"]!["greeting"]!.GetValue<string>().ShouldBe("hello");
        written.StatusCode.ShouldBe(HttpStatusCode.OK, await written.Content.ReadAsStringAsync(Token));
        await using var scope = host.Services.CreateAsyncScope();
        var settings = await scope.ServiceProvider.GetRequiredService<ISettings<SampleSettings>>().GetAsync(new SiteId(DefaultSite.Id), Token);
        (settings.Greeting, settings.MaxNotes).ShouldBe(("hi", 3));
    }

    [Fact]
    public async Task ThePluginsJobRunsAndItsEventReachesItsOutboxHandlerAndTheActivityLog()
    {
        var calls = new PluginCalls();
        // The test runs the job and drains the outbox itself; the host's own workers would race it for the job on a slow runner.
        await using var host = new FluentCmsTestHost()
            .WithServices(services => services.AddSingleton(calls))
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });
        var admin = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));

        await host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<SampleNoteJob, SampleNotePayload>(new SampleNotePayload("from a job"), cancellationToken: Token);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<JobExecutor>().RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token)).ShouldBeTrue();
        }

        await using var read = host.Services.CreateAsyncScope();
        (await read.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>().Set<SampleNote>().Select(note => note.Text).ToListAsync(Token)).ShouldBe(["from a job"]);

        IReadOnlyList<ActivityItem> items = [];
        for (var attempt = 0; attempt < 50 && (items.Count == 0 || !calls.All.Contains("sample.outbox:from a job")); attempt++)
        {
            await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
            items = (await admin.RunAsync<IActivityQuery, PagedResult<ActivityItem>>(query => query.QueryAsync(null, null, Token))).Items;
            if (items.Count == 0)
            {
                await Task.Delay(100, Token);
            }
        }

        calls.All.ShouldContain("sample.outbox:from a job");
        var item = items.ShouldHaveSingleItem();
        item.Action.ShouldBe("sample.note_added");
        item.TargetLabel.ShouldBe("from a job");
        item.Origin.ShouldBe(Origin.Job);
    }

    // #2885: the end of a plugin's own transaction cannot be seen, so a publish inside it is refused as on a core context.
    [Fact]
    public async Task APublishInsideATransactionOnThePluginsContextIsRefusedAndLeavesNoOutboxRow()
    {
        var calls = new PluginCalls();
        await using var host = new FluentCmsTestHost().WithServices(services => services.AddSingleton(calls));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>();
            var events = scope.ServiceProvider.GetRequiredService<IEventBus>();
            await using (var transaction = await database.Database.BeginTransactionAsync(Token))
            {
                var note = new SampleNote { Id = Guid.CreateVersion7(), Text = "rolled back", CreatedAt = host.Time.GetUtcNow() };
                database.Add(note);
                await database.SaveChangesAsync(Token);

                var refused = await Should.ThrowAsync<InvalidOperationException>(async () => await events.PublishAsync(new SampleNoteAdded(note.Id, note.Text), Token));
                refused.Message.ShouldStartWith("FCMS2602");
                await transaction.RollbackAsync(Token);
            }

            await events.PublishAsync(new SampleNoteAdded(Guid.CreateVersion7(), "after the transaction"), Token);
        }

        await using var read = host.Services.CreateAsyncScope();
        (await read.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>().Set<SampleNote>().CountAsync(Token)).ShouldBe(0);
        var rows = await read.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking().ToListAsync(Token);
        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(row => row.Payload.Contains("after the transaction"));
        calls.All.ShouldContain("sample.event:after the transaction");
        calls.All.ShouldNotContain("sample.event:rolled back");
    }

    [Fact]
    public async Task AFieldReferenceThatNamesNoSiblingStopsTheHost()
    {
        await using var host = WithMemo(new FluentCmsTestHost(), of: "missing");

        var exception = Should.Throw<SchemaValidationException>(() => host.Services);

        var error = exception.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("FCMS1520");
        error.Message.ShouldContain("'missing'");
        error.Message.ShouldContain("memo.summary");
    }

    [Fact]
    public async Task AFieldReferenceToAFieldTheCallerCannotReadIsLeftOutOfTheSchema()
    {
        await using var host = WithMemo(new FluentCmsTestHost(), of: "title");
        var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        using var role = await admin.PostAsync(
            new Uri("/api/management/v1/roles", UriKind.Relative),
            Json("""{ "name": "summary-reader", "grants": [ { "permission": "schema.read" }, { "permission": "content.memo.read", "constraints": { "fields": ["body", "summary"] } } ] }"""),
            Token);
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(Token));
        var reader = await host.AsUserAsync(await host.CreateUserAsync("summary-reader", cancellationToken: Token));

        var full = await TextAsync(admin, "schema/types/memo");
        JsonNode.Parse(full)!["fields"]!.AsArray().Single(field => field!["name"]!.GetValue<string>() == "summary")!["settings"]!["of"]!.GetValue<string>().ShouldBe("title");

        foreach (var path in new[] { "schema", "schema/types/memo", "schema/snapshot" })
        {
            var text = await TextAsync(reader, path);
            text.ShouldContain("\"summary\"");
            text.ShouldNotContain("\"title\"");
            text.ShouldNotContain("\"of\"");
        }

        var snapshot = await TextAsync(reader, "schema/snapshot");
        snapshot.ShouldBe(SchemaJsonWriter.Write(new SchemaBuilder(host.Services.GetRequiredService<IFieldTypeRegistry>()).AddSchemaJson(snapshot).Build()));
    }

    private static FluentCmsTestHost WithMemo(FluentCmsTestHost host, string of) =>
        host.WithServices(services => services.AddSingleton(provider => new SchemaBuilder(provider.GetRequiredService<IFieldTypeRegistry>())
            .AddCollection("memo", memo => memo
                .Field("title", "text")
                .Field("body", "text")
                .Field("summary", ExcerptFieldType.TypeName, summary => summary.Setting(ExcerptFieldType.Of, of)))
            .Build()));

    private static async Task<string> TextAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri($"/api/management/v1/{path}", UriKind.Relative), Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return text;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["code"]!.GetValue<string>();

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<List<string>> TablesAsync(DbContext context)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(Token);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            List<string> tables = [];
            await using var reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static void Refused(Action<FluentCmsBuilder> configure, string code, params string[] named)
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(() => new ServiceCollection().AddFluentCms(cms =>
        {
            cms.UseSqlite("Data Source=:memory:");
            configure(cms);
        }));

        exception.Code.ShouldBe(code);
        foreach (var name in named)
        {
            exception.Message.ShouldContain(name);
        }
    }
}
