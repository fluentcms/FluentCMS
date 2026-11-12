using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Core.Transfer;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Hooks;
using FluentCMS.Providers;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Settings;
using FluentCMS.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// Export and import (spec 4.14) between two databases of the provider of the run, through the services the endpoints call.
public sealed class TransferTests : IDisposable
{
    private const string Body = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hello"}]}]}""";

    private static readonly Principal Admin = AuthorizationDatabase.User([BuiltInRoles.SuperAdmin], [TokenScopes.Management]);
    private static readonly string[] Locales = ["en", "fr", "fr-CA"];

    private readonly ServiceDatabase _source = new(Configure);
    private readonly List<ServiceDatabase> _targets = [];

    public void Dispose()
    {
        _source.Dispose();
        foreach (var target in _targets)
        {
            target.Dispose();
        }
    }

    [Fact]
    public async Task AnImportIntoAnEmptyDatabaseReadsAsTheSourceInEveryLocaleAndState()
    {
        var ids = await SeedAsync();
        var target = Target();

        var result = await ImportAsync(target, await ExportAsync(_source));

        result.Items.Count(item => item.Section != TransferImporter.SettingsSection).ShouldBe(ids.Count);
        result.Items.Where(item => item.Section != TransferImporter.SettingsSection).ShouldAllBe(item => item.Outcome == TransferOutcome.Created);
        result.Total.Errors.ShouldBe(0);
        await ShouldReadAlikeAsync(_source, target, ids);
        target.Read(context => context.Versions.AsNoTracking().Select(version => version.Origin).Distinct().ToList()).ShouldBe([Origin.Import]);
    }

    [Fact]
    public async Task TheSamePackageImportedTwiceChangesNothingTheSecondTime()
    {
        var ids = await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target();
        await ImportAsync(target, package);
        var footprint = target.Footprint();

        var again = await ImportAsync(target, package);

        again.Items.ShouldAllBe(item => item.Outcome == TransferOutcome.Skipped);
        again.Items.Count(item => item.Section != TransferImporter.SettingsSection).ShouldBe(ids.Count);
        target.Footprint().ShouldBe(footprint);
    }

    [Fact]
    public async Task AnImportCutOffInTheMiddleIsFinishedByRunningItAgain()
    {
        var ids = await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target();
        using var cut = CancellationTokenSource.CreateLinkedTokenSource(Token);

        await Should.ThrowAsync<OperationCanceledException>(() => ImportAsync(target, package, progress: (line, _) =>
        {
            if (JsonSerializer.Serialize(line).Contains("\"section\":\"article\",\"counts\"", StringComparison.Ordinal))
            {
                cut.Cancel();
                cut.Token.ThrowIfCancellationRequested();
            }

            return Task.CompletedTask;
        }, token: cut.Token));
        target.Read(context => context.Documents.Count()).ShouldBeGreaterThan(0);

        var rest = await ImportAsync(target, package);

        rest.Items.Where(item => item.Section == "article").ShouldAllBe(item => item.Outcome == TransferOutcome.Skipped);
        rest.Items.Where(item => item.Section is "author" or "tag").ShouldAllBe(item => item.Outcome == TransferOutcome.Created);
        await ShouldReadAlikeAsync(_source, target, ids);
    }

    [Fact]
    public async Task ADryRunWritesNothingAndReportsWhatTheImportThenDoes()
    {
        await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target();
        var footprint = target.Footprint();

        var dry = await ImportAsync(target, package, new TransferImportRequest { DryRun = true });
        target.Footprint().ShouldBe(footprint);
        target.Read(context => context.Set<SettingRow>().Count()).ShouldBe(0);

        var real = await ImportAsync(target, package);

        dry.DryRun.ShouldBeTrue();
        Report(dry).ShouldBe(Report(real));
    }

    [Fact]
    public async Task AnEntryNewerInTheInstanceIsAConflictThatOverwriteSettles()
    {
        var ids = await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target();
        await ImportAsync(target, package);
        var author = ids.Single(id => id.Type == "author" && id.Name == "Ada").Id;
        var stored = (await target.GetAsync(Admin, author, type: "author"))!;
        await target.UpdateAsync(Admin, author, new ContentDocument { ["name"] = "Ada changed here" }, VersionOf(stored), "author");

        var kept = await ImportAsync(target, package);
        var taken = await ImportAsync(target, package, new TransferImportRequest { Overwrite = true });

        kept.Items.Single(item => item.Id == author.ToString("D")).Outcome.ShouldBe(TransferOutcome.Conflict);
        taken.Items.Single(item => item.Id == author.ToString("D")).Outcome.ShouldBe(TransferOutcome.Updated);
        (await target.GetAsync(Admin, author, type: "author"))!["name"].ShouldBe("Ada");
    }

    [Fact]
    public async Task AnEntryThatBreaksARuleOrThatAHookRefusesIsReportedAndTheOthersAreImported()
    {
        var source = Source(NoteSchema(maxLength: null));
        var good = await CreateAsSystemAsync(source, "note", new ContentDocument { ["title"] = "Fine" });
        var tooLong = await CreateAsSystemAsync(source, "note", new ContentDocument { ["title"] = "Far too long a title" });
        var refused = await CreateAsSystemAsync(source, "note", new ContentDocument { ["title"] = "Refuse" });
        var target = Target(NoteSchema(maxLength: 10), cms => cms.Services.AddScoped<IBeforeCreateHook<ContentDocument>, RefusingHook>());

        var result = await ImportAsync(target, await ExportAsync(source));

        result.Items.Single(item => item.Id == IdOf(good).ToString("D")).Outcome.ShouldBe(TransferOutcome.Created);
        var rule = result.Items.Single(item => item.Id == IdOf(tooLong).ToString("D"));
        rule.Outcome.ShouldBe(TransferOutcome.Error);
        rule.Code.ShouldBe(ValidationErrorCodes.MaxLength);
        var hook = result.Items.Single(item => item.Id == IdOf(refused).ToString("D"));
        hook.Outcome.ShouldBe(TransferOutcome.Error);
        hook.Code.ShouldBe(RefusingHook.Code);
        target.Read(context => context.Documents.Select(document => document.DocumentId).ToList()).ShouldBe([IdOf(good)]);
    }

    [Fact]
    public async Task APackageWhoseSchemaTheInstanceCannotHoldIsRefusedBeforeAnyWrite()
    {
        var source = Source(NoteSchema(maxLength: null, extra: "subtitle"));
        await CreateAsSystemAsync(source, "note", new ContentDocument { ["title"] = "One", ["subtitle"] = "Sub" });
        var package = await ExportAsync(source);
        var narrower = Target(NoteSchema(maxLength: null));

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(narrower, package));

        refused.Errors.ShouldContain(error => error.Code == TransferErrorCodes.SchemaIncompatible && error.Path == "types.note.subtitle");
        narrower.Read(context => context.Documents.IgnoreQueryFilters().Count() + context.Versions.Count()).ShouldBe(0);
        narrower.Read(context => context.Set<SettingRow>().Count()).ShouldBe(0);
    }

    [Fact]
    public async Task APackageWithFewerTypesAndFieldsThanTheInstanceIsTaken()
    {
        var source = Source(NoteSchema(maxLength: null));
        var note = await CreateAsSystemAsync(source, "note", new ContentDocument { ["title"] = "One" });
        var wider = Target(NoteSchema(maxLength: null, extra: "subtitle", extraType: true));

        var result = await ImportAsync(wider, await ExportAsync(source));

        result.Items.Single().Outcome.ShouldBe(TransferOutcome.Created);
        (await wider.GetAsync(Admin, IdOf(note), type: "note"))!["title"].ShouldBe("One");
    }

    [Fact]
    public async Task ALocaleTheSiteDoesNotAllowRefusesTheImportOrIsSkipped()
    {
        await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target();
        target.Read(context => context.Set<SiteLocale>().Where(locale => locale.Code == "fr-CA").ExecuteDelete());

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, package));
        var skipped = await ImportAsync(target, package, new TransferImportRequest { SkipMissingLocales = true });

        refused.Errors.ShouldContain(error => error.Code == TransferErrorCodes.LocaleNotInSite);
        skipped.Total.Errors.ShouldBe(0);
        target.Read(context => context.Documents.Count(document => document.Locale == "fr-CA")).ShouldBe(0);
        target.Read(context => context.Documents.Count(document => document.Locale == "fr")).ShouldBe(1);
    }

    [Fact]
    public async Task NoSecretOfASettingIsInAPackageAndAnImportKeepsTheInstancesOwn()
    {
        await _source.AsAsync(Principal.System, async services =>
        {
            var settings = services.GetRequiredService<FluentCMS.Core.Settings.SettingsService>();
            await settings.WriteAsync(settings.Require(MailSettings.Area), SiteA, """{"sender":"news@example.com","password":"source-only-value"}""", null, Token);
            return true;
        }, origin: Origin.Seed);
        var package = await ExportAsync(_source);
        var target = Target();

        var text = Entries(package);
        text.Values.ShouldAllBe(content => !content.Contains("source-only-value", StringComparison.Ordinal));
        text.Keys.ShouldAllBe(name => name == "manifest.json" || name == "schema.json" || name == "settings.json" || name.StartsWith("content/", StringComparison.Ordinal));
        JsonNode.Parse(text["settings.json"])!.AsArray().Single()!["value"]!["sender"]!.GetValue<string>().ShouldBe("news@example.com");

        var result = await ImportAsync(target, package);

        result.Items.Single().ShouldBe(new TransferItem(TransferImporter.SettingsSection, MailSettings.Area, TransferOutcome.Created, null, null));
        target.Read(context => context.Set<SettingRow>().Single().Json).ShouldContain("news@example.com");
    }

    [Fact]
    public async Task ACallerWithoutTransferManageIsRefusedAndASecondExportOfTheSiteWaits()
    {
        await Should.ThrowAsync<ManagementAccessDeniedException>(() => _source.AsAsync(Editor, services => services.GetRequiredService<TransferExporter>().PrepareAsync(new TransferExportRequest(), Token)));
        await Should.ThrowAsync<ManagementAccessDeniedException>(() => ImportAsync(_source, [], principal: Editor));

        await using var held = await _source.AsAsync(Admin, async services => (await services.GetRequiredService<IDistributedLock>().TryAcquireAsync($"transfer.export.{SiteA.Value:N}", TimeSpan.Zero, Token))!);
        var running = await Should.ThrowAsync<ValidationException>(() => ExportAsync(_source));

        running.Errors.Single().Code.ShouldBe(TransferErrorCodes.ExportRunning);
    }

    [Fact]
    public async Task TheImportOfAnEntryInTheTrashOrOfAnIdOfAnotherTypeIsReported()
    {
        var ids = await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target();
        await ImportAsync(target, package);
        var ada = ids.Single(id => id.Name == "Ada").Id;
        await target.DeleteAsync(Admin, ada, "author");

        var result = await ImportAsync(target, package);

        var trashed = result.Items.Single(item => item.Id == ada.ToString("D"));
        trashed.Outcome.ShouldBe(TransferOutcome.Error);
        trashed.Code.ShouldBe(TransferErrorCodes.InTrash);
    }

    // The source as the tests of the content service write it: authors, one in draft only, a global tag, the singleton,
    // and articles in three locales that are published, modified, unpublished or drafts.
    private async Task<List<(string Type, Guid Id, string Name)>> SeedAsync()
    {
        var ada = IdOf(await CreateAsSystemAsync(_source, "author", new ContentDocument { ["name"] = "Ada", ["secret"] = "hidden value" }));
        await PublishAsync(_source, "author", ada, null);
        var grace = IdOf(await CreateAsSystemAsync(_source, "author", new ContentDocument { ["name"] = "Grace" }));
        var tag = IdOf(await CreateAsSystemAsync(_source, "tag", new ContentDocument { ["name"] = "net", ["code"] = "t1" }));
        await PublishAsync(_source, "tag", tag, null);
        var settings = IdOf(await CreateAsSystemAsync(_source, "settings", new ContentDocument { ["name"] = "Site" }));
        await PublishAsync(_source, "settings", settings, null);

        var first = IdOf(await CreateAsSystemAsync(_source, "article", new ContentDocument
        {
            ["title"] = "First",
            ["body"] = new RichText(Body),
            ["author"] = ada,
            ["sponsor"] = grace,
            ["tags"] = new[] { tag },
            ["views"] = 7L,
            ["metadata"] = JsonNode.Parse("""{"a":[1,2]}"""),
            ["sections"] = JsonNode.Parse("""[{"type":"quote","text":"Quoted"}]"""),
            ["seo"] = new ContentDocument { ["metaTitle"] = "Meta", ["internal"] = "kept" },
            ["secret"] = "hidden",
        }));
        await PublishAsync(_source, "article", first, "en");
        await UpdateAsSystemAsync(_source, "article", first, new ContentDocument { ["title"] = "Premier" }, "fr");
        await PublishAsync(_source, "article", first, "fr");
        await UpdateAsSystemAsync(_source, "article", first, new ContentDocument { ["title"] = "Premier, revu" }, "fr");
        await UpdateAsSystemAsync(_source, "article", first, new ContentDocument { ["title"] = "Premier au Canada" }, "fr-CA");

        var second = IdOf(await CreateAsSystemAsync(_source, "article", new ContentDocument { ["title"] = "Second" }));
        await PublishAsync(_source, "article", second, "en");
        await _source.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().UnpublishAsync("article", second, null, Token), origin: Origin.Seed);

        return [("author", ada, "Ada"), ("author", grace, "Grace"), ("tag", tag, "net"), ("settings", settings, "Site"), ("article", first, "First"), ("article", second, "Second")];
    }

    // Every locale of every entry, in both states, as the content service reads it: ids, versions, times and fields.
    private static async Task ShouldReadAlikeAsync(ServiceDatabase source, ServiceDatabase target, List<(string Type, Guid Id, string Name)> ids)
    {
        foreach (var (type, id, _) in ids)
        {
            foreach (var locale in Locales)
            {
                foreach (var status in new[] { QueryStatus.Draft, QueryStatus.Published })
                {
                    var options = new GetOptions { Status = status, Locale = locale, Fallback = LocaleFallback.None };
                    var expected = await source.AsAsync(Admin, services => services.GetRequiredService<IContentService>().GetAsync(type, id, options, Token));
                    var actual = await target.AsAsync(Admin, services => services.GetRequiredService<IContentService>().GetAsync(type, id, options, Token));
                    Json(actual).ShouldBe(Json(expected), $"{type} {id} {locale} {status}");
                }
            }
        }

        var sourceVersions = source.Read(context => context.Documents.AsNoTracking().OrderBy(document => document.DocumentId).ThenBy(document => document.Locale).Select(document => new { document.DocumentId, document.Locale, document.Status, document.DraftVersion, document.PublishedVersion, document.FirstPublishedAt }).ToList());
        var targetVersions = target.Read(context => context.Documents.AsNoTracking().OrderBy(document => document.DocumentId).ThenBy(document => document.Locale).Select(document => new { document.DocumentId, document.Locale, document.Status, document.DraftVersion, document.PublishedVersion, document.FirstPublishedAt }).ToList());
        JsonSerializer.Serialize(targetVersions).ShouldBe(JsonSerializer.Serialize(sourceVersions));
    }

    private static string? Json(ContentDocument? entry) => EntryJson.ToNode(entry)?.ToJsonString();

    private static string Report(TransferResult result) => JsonSerializer.Serialize(result with { DryRun = false }, TransferPackage.Json);

    private static Dictionary<string, string> Entries(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        });
    }

    private static Task<byte[]> ExportAsync(ServiceDatabase database, TransferExportRequest? request = null) =>
        database.AsAsync(Admin, async services =>
        {
            var exporter = services.GetRequiredService<TransferExporter>();
            var run = await exporter.PrepareAsync(request ?? new TransferExportRequest(), Token);
            using var stream = new MemoryStream();
            await exporter.WriteAsync(run, stream, Token);
            return stream.ToArray();
        });

    private static Task<TransferResult> ImportAsync(ServiceDatabase database, byte[] package, TransferImportRequest? request = null, Func<object, CancellationToken, Task>? progress = null, CancellationToken? token = null, Principal? principal = null) =>
        database.AsAsync(principal ?? Admin, async services =>
        {
            var importer = services.GetRequiredService<TransferImporter>();
            await using var run = await importer.PrepareAsync(new MemoryStream(package), package.Length, request ?? new TransferImportRequest(), token ?? Token);
            return await importer.RunAsync(run, progress ?? ((_, _) => Task.CompletedTask), token ?? Token);
        });

    private static Task<ContentDocument> CreateAsSystemAsync(ServiceDatabase database, string type, ContentDocument entry, string locale = "en") =>
        database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().CreateAsync(type, entry, Token), locale, origin: Origin.Seed);

    private static Task<ContentDocument> UpdateAsSystemAsync(ServiceDatabase database, string type, Guid id, ContentDocument patch, string locale) =>
        database.AsAsync(Principal.System, async services =>
        {
            var content = services.GetRequiredService<IContentService>();
            var version = await database.AsAsync(Principal.System, inner => Task.FromResult(inner.GetRequiredService<CmsDbContext>().Documents.AsNoTracking().Where(document => document.DocumentId == id && document.Locale == locale).Select(document => document.DraftVersion).FirstOrDefault()), locale, origin: Origin.Seed);
            return await content.PatchAsync(type, id, patch, version, Token);
        }, locale, origin: Origin.Seed);

    private static Task<PublishResult<ContentDocument>> PublishAsync(ServiceDatabase database, string type, Guid id, string? locale) =>
        database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().PublishAsync(type, id, new LocaleWriteOptions { Locale = locale }, Token), locale ?? "en", origin: Origin.Seed);

    private ServiceDatabase Source(SchemaModel schema)
    {
        var source = new ServiceDatabase(Configure, schema);
        _targets.Add(source);
        return source;
    }

    private ServiceDatabase Target(SchemaModel? schema = null, Action<FluentCmsBuilder>? configure = null)
    {
        var target = new ServiceDatabase(
            cms =>
            {
                Configure(cms);
                configure?.Invoke(cms);
            },
            schema);
        _targets.Add(target);
        return target;
    }

    private static void Configure(FluentCmsBuilder cms) => cms.Settings.Add<MailSettings>();

    private static SchemaModel NoteSchema(int? maxLength, string? extra = null, bool extraType = false)
    {
        var builder = SampleSchema.NewBuilder()
            .AddCollection("note", type =>
            {
                type.Field("title", "text", field => field.Length(null, maxLength));
                if (extra is not null)
                {
                    type.Field(extra, "text");
                }
            });
        if (extraType)
        {
            builder.AddCollection("memo", type => type.Field("text", "text"));
        }

        return builder.Build();
    }

    [Settings(Area)]
    public sealed class MailSettings
    {
        public const string Area = "transfer-mail";

        public string Sender { get; set; } = "";

        [SettingsSecret]
        public string Password { get; set; } = "";
    }

    public sealed class RefusingHook : IBeforeCreateHook<ContentDocument>
    {
        public const string Code = "FCMS9902";

        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (context.Entry.TryGetValue("title", out var title) && Equals(title, "Refuse"))
            {
                context.Cancel(Code, "The test refuses this entry.");
            }

            return ValueTask.CompletedTask;
        }
    }
}
