using System.Data.Common;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Core.Tests.Transfer;
using FluentCMS.Core.Transfer;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Hooks;
using FluentCMS.Providers;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Settings;
using FluentCMS.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

        result.Items.ShouldBeEmpty();
        ContentCounts(result).ShouldBe(TransferCounts.None with { Created = ids.Count });
        result.Total.Errors.ShouldBe(0);
        await ShouldReadAlikeAsync(_source, target, ids);
        target.Read(context => context.Versions.AsNoTracking().Select(version => version.Origin).Distinct().ToList()).ShouldBe([Origin.Import]);
    }

    // #3305: a batch of entries is read in a fixed number of queries, so the export of 150 entries costs one batch more than
    // the export of three, where it read each entry's rows one by one before.
    [Fact]
    public async Task AnExportReadsItsEntriesInQueriesThatGrowWithTheBatchesNotTheEntries()
    {
        var trips = new RoundTrips();
        var source = Source(trips);
        var request = new TransferExportRequest { Types = ["author"] };

        async Task<int> ReadsAsync(int count)
        {
            for (var index = 0; index < count; index++)
            {
                await CreateAsSystemAsync(source, "author", new ContentDocument { ["name"] = $"Author {index}" });
            }

            await trips.CountAsync(() => ExportAsync(source, request));
            return trips.Sent.Count(sql => sql.Contains("cms_document", StringComparison.Ordinal) || sql.Contains("cms_author", StringComparison.Ordinal) || sql.Contains("cms_reference", StringComparison.Ordinal));
        }

        var few = await ReadsAsync(3);
        var many = await ReadsAsync(147);

        few.ShouldBeLessThanOrEqualTo(5, string.Join('\n', trips.Sent));
        many.ShouldBeLessThanOrEqualTo(few + 4, string.Join('\n', trips.Sent));
    }

    // #3305: the entries of an export are read from one snapshot, so a save made while it reads changes nothing in the
    // package: neither the entry it changes nor the one it adds.
    [Fact]
    public async Task ASaveDuringAnExportDoesNotChangeWhatThePackageHolds()
    {
        var save = new SaveDuringExport();
        var source = Source(save);
        if (TestProvider.IsSqlServer)
        {
            // The migrations turn on READ_COMMITTED_SNAPSHOT only; SNAPSHOT isolation is the administrator's to allow.
            source.Read(context => context.Database.ExecuteSqlRaw("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON"));
        }

        var ada = IdOf(await CreateAsSystemAsync(source, "author", new ContentDocument { ["name"] = "Ada" }));
        var grace = IdOf(await CreateAsSystemAsync(source, "author", new ContentDocument { ["name"] = "Grace" }));
        var before = Entries(await ExportAsync(source, new TransferExportRequest { Types = ["author"] }))[TransferPackage.ContentPath("author")];

        save.Run = async () =>
        {
            var version = source.Read(context => context.Documents.AsNoTracking().Single(document => document.DocumentId == ada).DraftVersion);
            await source.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().PatchAsync("author", ada, new ContentDocument { ["name"] = "Ada changed" }, version, Token), origin: Origin.Seed);
            await CreateAsSystemAsync(source, "author", new ContentDocument { ["name"] = "Hopper" });
        };
        var during = Entries(await ExportAsync(source, new TransferExportRequest { Types = ["author"] }))[TransferPackage.ContentPath("author")];

        save.Run.ShouldBeNull();
        during.ShouldBe(before);
        during.ShouldContain(grace.ToString("D"));
        Entries(await ExportAsync(source, new TransferExportRequest { Types = ["author"] }))[TransferPackage.ContentPath("author")].ShouldContain("Ada changed");
    }

    // #3305: a line of a type that is not localized names a locale only in a package made by hand; it is reported as an
    // invalid item, not as an unexpected failure, and the other lines are imported.
    [Fact]
    public async Task ALocaleOnAnEntryOfATypeThatIsNotLocalizedIsAnInvalidItem()
    {
        await SeedAsync();
        var exported = await ExportAsync(_source);
        var lines = Entries(exported)[TransferPackage.ContentPath("author")].Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var line = JsonNode.Parse(lines[0])!;
        line["locales"]![0]!["locale"] = "en";
        var target = Target();

        var result = await ImportAsync(target, Rewrite(exported, TransferPackage.ContentPath("author"), string.Join('\n', [line.ToJsonString(), .. lines.Skip(1)])));

        var item = result.Items.Single(item => item.Section == "author");
        (item.Id, item.Outcome, item.Code).ShouldBe((line["id"]!.GetValue<string>(), TransferOutcome.Error, TransferErrorCodes.InvalidItem));
        item.Message.ShouldNotBeNull().ShouldContain("not localized");
        result.Sections["author"].ShouldBe(TransferCounts.None with { Created = lines.Length - 1, Errors = 1 });
    }

    // #3305: authors=importer records whoever imports as the creator and updater of what the import writes; by default the
    // package's authors are kept.
    [Fact]
    public async Task AuthorsImporterRecordsTheImporterAsCreatorAndUpdater()
    {
        await SeedAsync();
        var package = await ExportAsync(_source);
        var kept = Target();
        var mine = Target();

        await ImportAsync(kept, package);
        await ImportAsync(mine, package, new TransferImportRequest { AuthorsFromImporter = true });

        var source = _source.Read(context => context.Documents.AsNoTracking().Select(document => new { document.CreatedBy, document.UpdatedBy }).Distinct().ToList());
        source.ShouldNotContain(row => row.CreatedBy == Admin.Id || row.UpdatedBy == Admin.Id);
        kept.Read(context => context.Documents.AsNoTracking().Select(document => new { document.CreatedBy, document.UpdatedBy }).Distinct().ToList()).ShouldBe(source, ignoreOrder: true);
        mine.Read(context => context.Documents.AsNoTracking().Select(document => new { document.CreatedBy, document.UpdatedBy }).Distinct().ToList())
            .ShouldHaveSingleItem().ShouldBe(new { CreatedBy = Admin.Id, UpdatedBy = Admin.Id });
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

        again.Items.ShouldBeEmpty();
        again.Total.ShouldBe(TransferCounts.None with { Skipped = again.Total.Skipped });
        ContentCounts(again).ShouldBe(TransferCounts.None with { Skipped = ids.Count });
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

        rest.Sections["article"].ShouldBe(TransferCounts.None with { Skipped = 2 });
        rest.Sections["author"].ShouldBe(TransferCounts.None with { Created = 2 });
        rest.Sections["tag"].ShouldBe(TransferCounts.None with { Created = 1 });
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

    // #3265: the package names en alone, and moves a shared field under fr, which the instance's entry has too.
    [Fact]
    public async Task AnImportThatMovesASharedFieldAdvancesTheLocalesThePackageDoesNotName()
    {
        var id = IdOf(await _source.CreateAsync(Admin, new() { ["title"] = "Hello", ["views"] = 1 }));
        var target = Target();
        await ImportAsync(target, await ExportAsync(_source));
        await target.UpdateAsync(Admin, id, new ContentDocument { ["title"] = "Bonjour" }, 0, locale: "fr");
        await _source.PatchAsync(Admin, id, new() { ["views"] = 2 }, 1);

        await ImportAsync(target, await ExportAsync(_source), new TransferImportRequest { Overwrite = true });

        var french = target.Versions(id).Where(version => version.Locale == "fr").ToList();
        french.Select(version => version.Number).ShouldBe([1, 2]);
        french[^1].Message.ShouldBe("Shared fields changed in en.");
        (await target.GetAsync(Admin, id, locale: "fr"))!["views"].ShouldBe(2L);
        await Should.ThrowAsync<VersionConflictException>(() => target.PatchAsync(Admin, id, new() { ["views"] = 3 }, 1, locale: "fr"));
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
        taken.Items.ShouldBeEmpty();
        taken.Sections["author"].Updated.ShouldBe(1);
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

        result.Items.ShouldNotContain(item => item.Id == IdOf(good).ToString("D"));
        result.Sections["note"].ShouldBe(TransferCounts.None with { Created = 1, Errors = 2 });
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

        result.Items.ShouldBeEmpty();
        result.Sections["note"].ShouldBe(TransferCounts.None with { Created = 1 });
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

        result.Items.ShouldBeEmpty();
        result.Sections.ShouldHaveSingleItem().ShouldBe(new KeyValuePair<string, TransferCounts>(TransferImporter.SettingsSection, TransferCounts.None with { Created = 1 }));
        target.Read(context => context.Set<SettingRow>().Single().Json).ShouldContain("news@example.com");

        // The site of the target holds no password: it reads that of the target's instance defaults.
        target.Read(context => context.Set<SettingRow>().Single().Json).ShouldNotContain("password");
    }

    // The checks of an area hold for an import as for a write, in a dry run too: a preview template that is not an http URL.
    [Fact]
    public async Task ASettingsDocumentThatTheChecksOfItsAreaRefuseIsReportedByADryRun()
    {
        var package = Rewrite(await ExportAsync(_source), TransferPackage.Settings, """[{"area":"preview","value":{"templates":{"article":"ftp://www.example.com/?preview={token}"}}}]""");
        var target = Target();

        var dry = await ImportAsync(target, package, new TransferImportRequest { DryRun = true });

        var item = dry.Items.ShouldHaveSingleItem();
        (item.Section, item.Id, item.Outcome, item.Code).ShouldBe((TransferImporter.SettingsSection, "preview", TransferOutcome.Error, PreviewTokenErrorCodes.InvalidUrlTemplate));
        item.Message.ShouldStartWith("templates.article: ");
        target.Read(context => context.Set<SettingRow>().Count()).ShouldBe(0);
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
    public async Task GlobalTypesNeedTransferManageForTheInstanceAndTheOtherTypesAreTakenWithout()
    {
        await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target();
        var siteRole = AddRole(target, Guid.CreateVersion7(), SiteA.Value, CorePermissions.TransferManage);
        var siteAdmin = WithRole(siteRole);

        var denied = await Should.ThrowAsync<ManagementAccessDeniedException>(() => ImportAsync(target, package, principal: siteAdmin));
        var taken = await ImportAsync(target, package, new TransferImportRequest { Types = ["article", "author", "settings"] }, principal: siteAdmin);

        denied.Message.ShouldContain("types= of the endpoint or --types of fluentcms import");
        taken.Total.Errors.ShouldBe(0);
        taken.Sections.ShouldNotContainKey("tag");
        target.Read(context => context.Documents.Count(document => document.Type == "tag")).ShouldBe(0);
    }

    [Fact]
    public async Task AGlobalTypeWhoseStoredFileDeclaresNoBytesStillNeedsTransferManageForTheInstanceAndNothingIsWritten()
    {
        await SeedAsync();
        var package = Craft(await ExportAsync(_source), TransferPackage.ContentPath("tag"), 0);
        using (var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            archive.GetEntry(TransferPackage.ContentPath("tag"))!.Length.ShouldBe(0);
        }

        var target = Target();
        var siteAdmin = WithRole(AddRole(target, Guid.CreateVersion7(), SiteA.Value, CorePermissions.TransferManage));

        await Should.ThrowAsync<ManagementAccessDeniedException>(() => ImportAsync(target, package, new TransferImportRequest { Overwrite = true }, principal: siteAdmin));

        target.Read(context => context.Documents.Count()).ShouldBe(0);
    }

    [Fact]
    public async Task AGlobalTypeWithAnEmptyFileStillNeedsTransferManageForTheInstanceAndTheOtherTypesAreTakenWithTypes()
    {
        await CreateAsSystemAsync(_source, "author", new ContentDocument { ["name"] = "Ada" });
        var package = await ExportAsync(_source);
        Entries(package)[TransferPackage.ContentPath("tag")].ShouldBeEmpty();
        var target = Target();
        var siteAdmin = WithRole(AddRole(target, Guid.CreateVersion7(), SiteA.Value, CorePermissions.TransferManage));

        await Should.ThrowAsync<ManagementAccessDeniedException>(() => ImportAsync(target, package, principal: siteAdmin));
        var taken = await ImportAsync(target, package, new TransferImportRequest { Types = ["author"] }, principal: siteAdmin);

        taken.Total.Created.ShouldBe(1);
        taken.Total.Errors.ShouldBe(0);
        target.Read(context => context.Documents.Count(document => document.Type == "author")).ShouldBe(1);
    }

    [Theory]
    [InlineData("content/tag.jsonl", 0u)]
    [InlineData("content/article.jsonl", 10u)]
    [InlineData("manifest.json", 10u)]
    [InlineData("settings.json", 0u)]
    public async Task AFileThatHoldsMoreThanItsZipEntryDeclaresIsRefusedBeforeAnyWrite(string path, uint declared)
    {
        await SeedAsync();
        var package = Craft(await ExportAsync(_source), path, declared);
        var target = Target();

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, package, new TransferImportRequest { Overwrite = true }));

        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        refused.Errors.Single().Message.ShouldContain(path);
        target.Read(context => context.Documents.Count()).ShouldBe(0);
    }

    [Fact]
    public async Task APackageWithACraftedFileIsRefusedWholeEvenWhenTypesLeavesItsTypeOut()
    {
        await SeedAsync();
        var package = Craft(await ExportAsync(_source), TransferPackage.ContentPath("tag"), 0);
        var target = Target();
        var siteAdmin = WithRole(AddRole(target, Guid.CreateVersion7(), SiteA.Value, CorePermissions.TransferManage));

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, package, new TransferImportRequest { Types = ["article", "author", "settings"] }, principal: siteAdmin));

        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        refused.Errors.Single().Message.ShouldContain(TransferPackage.ContentPath("tag"));
        target.Read(context => context.Documents.Count()).ShouldBe(0);
    }

    // What the central directory shows is refused before any file is read, a dry run as well.
    [Theory]
    [InlineData(nameof(TransferOptions.MaxEntries), false)]
    [InlineData(nameof(TransferOptions.MaxEntries), true)]
    [InlineData(nameof(TransferOptions.MaxUnpackedSize), false)]
    [InlineData(nameof(TransferOptions.MaxUnpackedSize), true)]
    [InlineData(nameof(TransferOptions.MaxJsonSize), false)]
    [InlineData(nameof(TransferOptions.MaxJsonSize), true)]
    public async Task APackageOverABoundOfItsCentralDirectoryIsRefusedBeforeAnyWrite(string bound, bool dryRun)
    {
        await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target(configure: cms => cms.Services.Configure<TransferOptions>(options =>
        {
            options.MaxEntries = bound == nameof(TransferOptions.MaxEntries) ? 3 : options.MaxEntries;
            options.MaxUnpackedSize = bound == nameof(TransferOptions.MaxUnpackedSize) ? 1000 : null;
            options.MaxJsonSize = bound == nameof(TransferOptions.MaxJsonSize) ? 100 : options.MaxJsonSize;
        }));

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, package, new TransferImportRequest { DryRun = dryRun }));

        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        refused.Errors.Single().Message.ShouldContain($"FluentCms:Transfer:{bound}");
        target.Read(context => context.Documents.Count() + context.Set<SettingRow>().Count()).ShouldBe(0);
    }

    // Many files of a zip can point at the data of one, which an import would read once for each.
    [Fact]
    public async Task FilesWhoseCompressedSizesAddUpToMoreThanThePackageAreRefusedBeforeAnyWrite()
    {
        await SeedAsync();
        var exported = await ExportAsync(_source);
        var package = ZipCentralDirectory.DeclareCompressed(exported, TransferPackage.ContentPath("article"), (uint)exported.Length);
        var target = Target();

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, package));

        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        refused.Errors.Single().Message.ShouldContain("share their data");
        target.Read(context => context.Documents.Count()).ShouldBe(0);
    }

    [Fact]
    public async Task AFileOfACompressionMethodThatCannotBeReadIsRefusedBeforeAnyWriteWhateverTypesSays()
    {
        await SeedAsync();
        var exported = await ExportAsync(_source);
        using (var archive = new ZipArchive(new MemoryStream(exported), ZipArchiveMode.Read))
        {
            var tag = archive.GetEntry(TransferPackage.ContentPath("tag"))!;
            tag.CompressedLength.ShouldBeLessThanOrEqualTo(tag.Length);
        }

        var package = ZipCentralDirectory.Method(exported, TransferPackage.ContentPath("tag"), 14);
        var target = Target();

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, package, new TransferImportRequest { Types = ["article"] }));

        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        refused.Errors.Single().Message.ShouldContain(TransferPackage.ContentPath("tag"));
        target.Read(context => context.Documents.Count() + context.Set<SettingRow>().Count()).ShouldBe(0);
    }

    // schema.json is read only when its hash is not the instance's, so a file that cannot be read is no matter then.
    [Fact]
    public async Task TheSchemaOfThePackageIsReadOnlyWhenItsHashIsNotTheInstances()
    {
        await CreateAsSystemAsync(_source, "author", new ContentDocument { ["name"] = "Ada" });
        var package = ZipCentralDirectory.Corrupt(await ExportAsync(_source), TransferPackage.Schema);
        var same = Target();
        var other = Target(NoteSchema(maxLength: null));

        var taken = await ImportAsync(same, package);
        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(other, package));

        taken.Sections["author"].Created.ShouldBe(1);
        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        refused.Errors.Single().Message.ShouldContain(TransferPackage.Schema);
    }

    // A bound that only reading shows stops the import where it is reached: what was written stays, and the last item of the
    // report, which a dry run gives alike, says why.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALineLongerThanTheLimitStopsTheImportWithTheErrorAsTheLastItem(bool dryRun)
    {
        await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target(configure: cms => cms.Services.Configure<TransferOptions>(options => options.MaxLineLength = 200));

        var result = await ImportAsync(target, package, new TransferImportRequest { DryRun = dryRun });

        result.Items[^1].ShouldBe(new TransferItem(TransferImporter.PackageSection, TransferPackage.ContentPath("article"), TransferOutcome.Error, TransferErrorCodes.InvalidPackage, result.Items[^1].Message));
        result.Items[^1].Message!.ShouldContain("FluentCms:Transfer:MaxLineLength");
        result.Sections.Keys.Where(section => section != TransferImporter.SettingsSection).ShouldBe([TransferImporter.PackageSection]);
        target.Read(context => context.Documents.Count()).ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheImportStopsAtTheFirstFileThatTakesItOverTheCompressedBytesItMayRead(bool dryRun)
    {
        await SeedAsync();
        var package = await ExportAsync(_source);
        long read;
        using (var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            read = new[] { TransferPackage.Manifest, TransferPackage.Settings, TransferPackage.ContentPath("article") }.Sum(path => archive.GetEntry(path)!.CompressedLength);
        }

        var target = Target(configure: cms => cms.Services.Configure<TransferOptions>(options => options.MaxReadSize = read));

        var result = await ImportAsync(target, package, new TransferImportRequest { DryRun = dryRun });

        result.Sections["article"].ShouldBe(TransferCounts.None with { Created = 2 });
        result.Sections.ShouldNotContainKey("author");
        var stop = result.Items.ShouldHaveSingleItem();
        (stop.Section, stop.Id, stop.Code).ShouldBe((TransferImporter.PackageSection, TransferPackage.ContentPath("author"), TransferErrorCodes.InvalidPackage));
        stop.Message!.ShouldContain("FluentCms:Transfer:MaxReadSize");
        target.Read(context => context.Documents.Select(document => document.Type).Distinct().ToList()).ShouldBe(dryRun ? [] : ["article"]);
    }

    [Fact]
    public async Task AFileWhoseCompressedDataCannotBeReadStopsTheImportWithTheErrorAsTheLastItem()
    {
        await SeedAsync();
        var package = ZipCentralDirectory.Corrupt(await ExportAsync(_source), TransferPackage.ContentPath("author"));
        var target = Target();

        var result = await ImportAsync(target, package);

        var stop = result.Items[^1];
        (stop.Section, stop.Id, stop.Code).ShouldBe((TransferImporter.PackageSection, TransferPackage.ContentPath("author"), TransferErrorCodes.InvalidPackage));
        result.Sections["article"].ShouldBe(TransferCounts.None with { Created = 2 });
        result.Sections.ShouldNotContainKey("author");
    }

    [Fact]
    public async Task TheReportListsNoMoreErrorsThanItsLimitAndCountsThemAll()
    {
        await SeedAsync();
        var package = Rewrite(await ExportAsync(_source), TransferPackage.ContentPath("article"), "x\ny\n{}\n[]\n");
        var target = Target(configure: cms => cms.Services.Configure<TransferOptions>(options => options.MaxReportItems = 2));

        var result = await ImportAsync(target, package);

        result.Items.Count.ShouldBe(2);
        result.Omitted.ShouldBe(2);
        result.Items.ShouldAllBe(item => item.Section == "article" && item.Code == TransferErrorCodes.InvalidItem);
        result.Sections["article"].ShouldBe(TransferCounts.None with { Errors = 4 });
        result.Sections["author"].ShouldBe(TransferCounts.None with { Created = 2 });
    }

    // One line of many values can break a rule once for each, and its item names the first of them only.
    [Fact]
    public async Task TheItemOfAnEntryWithManyErrorsNamesTheFirstTwentyAndCountsTheRest()
    {
        await SeedAsync();
        var exported = await ExportAsync(_source);
        var line = JsonNode.Parse(Entries(exported)[TransferPackage.ContentPath("article")].Split('\n')[0])!;
        foreach (var locale in line["locales"]!.AsArray())
        {
            if (locale!["draft"] is JsonObject draft)
            {
                draft["tags"] = new JsonArray([.. Enumerable.Repeat(1, 20_000).Select(value => (JsonNode)JsonValue.Create(value))]);
            }
        }

        var target = Target();

        var result = await ImportAsync(target, Rewrite(exported, TransferPackage.ContentPath("article"), line.ToJsonString()));

        var item = result.Items.Single(item => item.Section == "article");
        item.Outcome.ShouldBe(TransferOutcome.Error);
        item.Message!.Length.ShouldBeLessThanOrEqualTo(TransferReport.MaxTextLength + 1);
        item.Message.ShouldEndWith(" more");
        item.Message.Split("; ").Length.ShouldBe(21);
    }

    [Fact]
    public async Task RolesComeWithTheirIdsOnlyWhenAskedAndOnlyAsFarAsTheCallerHoldsThem()
    {
        var own = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        AddRole(_source, own, SiteA.Value, CorePermissions.TransferManage, CorePermissions.RolesManage, CorePermissions.UsersManage);
        AddRole(_source, other, SiteA.Value, "content.*.read");
        var package = await ExportAsync(_source, new TransferExportRequest { Roles = true });
        var target = Target();
        AddRole(target, own, SiteA.Value, CorePermissions.TransferManage);

        var without = await ImportAsync(target, package);
        var limited = await ImportAsync(target, package, new TransferImportRequest { Roles = true, Types = ["author"] }, principal: WithRole(own));
        var full = await ImportAsync(target, package, new TransferImportRequest { Roles = true });

        without.Sections.ShouldNotContainKey(TransferImporter.RolesSection);
        limited.Sections[TransferImporter.RolesSection].ShouldBe(TransferCounts.None with { Errors = 2 });
        limited.Items.Where(item => item.Section == TransferImporter.RolesSection).ShouldAllBe(item => item.Outcome == TransferOutcome.Error && item.Code == "FCMS3103");
        full.Sections[TransferImporter.RolesSection].ShouldBe(TransferCounts.None with { Created = 1, Updated = 1 });
        full.Items.ShouldNotContain(item => item.Section == TransferImporter.RolesSection);
        target.Read(context => context.Set<RoleGrant>().Count(grant => grant.RoleId == own)).ShouldBe(3);
        target.Read(context => context.Set<Role>().Single(role => role.Id == other).SiteId).ShouldBe(SiteA.Value);
    }

    [Fact]
    public async Task AFieldThePackageDoesNotHaveKeepsWhatTheInstanceHolds()
    {
        var source = Source(NoteSchema(maxLength: null));
        var note = IdOf(await CreateAsSystemAsync(source, "note", new ContentDocument { ["title"] = "One" }));
        var package = await ExportAsync(source);
        var wider = Target(NoteSchema(maxLength: null, extra: "subtitle"));
        await ImportAsync(wider, package);
        var stored = (await wider.GetAsync(Admin, note, type: "note"))!;
        await wider.UpdateAsync(Admin, note, new ContentDocument { ["title"] = "Changed", ["subtitle"] = "Only here" }, VersionOf(stored), "note");

        var result = await ImportAsync(wider, package, new TransferImportRequest { Overwrite = true });

        result.Sections["note"].ShouldBe(TransferCounts.None with { Updated = 1 });
        var read = (await wider.GetAsync(Admin, note, type: "note"))!;
        read["title"].ShouldBe("One");
        read["subtitle"].ShouldBe("Only here");
    }

    [Fact]
    public async Task ADraftThatOverwritesAPublishedEntryIsADraftAgainAndThenSkipped()
    {
        var source = Source(NoteSchema(maxLength: null));
        var note = IdOf(await CreateAsSystemAsync(source, "note", new ContentDocument { ["title"] = "One" }));
        var package = await ExportAsync(source);
        var target = Target(NoteSchema(maxLength: null));
        await ImportAsync(target, package);
        await PublishAsync(target, "note", note, null);

        var overwritten = await ImportAsync(target, package, new TransferImportRequest { Overwrite = true });
        var again = await ImportAsync(target, package);

        overwritten.Sections["note"].ShouldBe(TransferCounts.None with { Updated = 1 });
        again.Sections["note"].ShouldBe(TransferCounts.None with { Skipped = 1 });
        target.Read(context => context.Documents.Single().Status).ShouldBe(EntryStatus.Draft);
    }

    [Fact]
    public async Task ASecondImportOfTheSiteWhileOneRunsIsRefusedWithItsCode()
    {
        await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target();
        await using var held = await target.AsAsync(Admin, async services => (await services.GetRequiredService<IDistributedLock>().TryAcquireAsync($"transfer.import.{SiteA.Value:N}", TimeSpan.Zero, Token))!);

        var running = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, package));

        running.Errors.Single().Code.ShouldBe(TransferErrorCodes.ImportRunning);
    }

    [Fact]
    public async Task APackageWhoseRolesCannotBeReadIsRefusedBeforeAnyWrite()
    {
        AddRole(_source, Guid.CreateVersion7(), SiteA.Value, "content.*.read");
        await CreateAsSystemAsync(_source, "author", new ContentDocument { ["name"] = "Ada" });
        var package = Rewrite(await ExportAsync(_source, new TransferExportRequest { Roles = true }), TransferPackage.Roles, "[{\"id\":");
        var target = Target();

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, package, new TransferImportRequest { Roles = true }));

        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        target.Read(context => context.Documents.Count()).ShouldBe(0);
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

    // Another holder took the lock after the lease could not be renewed: the import stops as a cancelled one does.
    [Fact]
    public async Task AnImportWhoseLockIsLostStopsBeforeItWritesMore()
    {
        await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target(configure: cms => cms.Providers.Use<IDistributedLock, LosingLock>());
        var locks = await target.AsAsync(Admin, services => Task.FromResult((LosingLock)services.GetRequiredService<IDistributedLock>()));

        await Should.ThrowAsync<OperationCanceledException>(() => ImportAsync(target, package, progress: (_, _) =>
        {
            locks.Lose();
            return Task.CompletedTask;
        }));

        target.Read(context => context.Documents.Count()).ShouldBe(0);
    }

    [Fact]
    public async Task TheLockOfTheSiteIsTakenOnceTheBodyIsIn()
    {
        await SeedAsync();
        var package = await ExportAsync(_source);
        var target = Target();
        var name = $"transfer.import.{SiteA.Value:N}";

        var (whileUploading, whileRunning) = await target.AsAsync(Admin, async services =>
        {
            var locks = services.GetRequiredService<IDistributedLock>();
            var free = false;
            var body = new ProbedStream(package, async () =>
            {
                await using var probe = await locks.TryAcquireAsync(name, TimeSpan.Zero, Token);
                free = probe is not null;
            });
            await using var run = await services.GetRequiredService<TransferImporter>().PrepareAsync(body, null, new TransferImportRequest(), Token);
            await using var held = await locks.TryAcquireAsync(name, TimeSpan.Zero, Token);
            return (free, held is null);
        });

        whileUploading.ShouldBeTrue();
        whileRunning.ShouldBeTrue();
    }

    [Theory]
    [InlineData("site")]
    [InlineData("locales")]
    public async Task AManifestWithoutItsSiteOrLocalesIsRefusedBeforeAnyWrite(string property)
    {
        var role = AddRole(_source, Guid.CreateVersion7(), SiteA.Value, "content.*.read");
        await _source.AsAsync(Principal.System, async services =>
        {
            var settings = services.GetRequiredService<FluentCMS.Core.Settings.SettingsService>();
            await settings.WriteAsync(settings.Require(MailSettings.Area), SiteA, """{"sender":"news@example.com"}""", null, Token);
            return true;
        }, origin: Origin.Seed);
        await SeedAsync();
        var exported = await ExportAsync(_source, new TransferExportRequest { Roles = true });
        var manifest = JsonNode.Parse(Entries(exported)[TransferPackage.Manifest])!.AsObject();
        manifest.Remove(property);
        var target = Target();

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, Rewrite(exported, TransferPackage.Manifest, manifest.ToJsonString()), new TransferImportRequest { Roles = true }));

        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        refused.Errors.Single().Message.ShouldContain(TransferPackage.Manifest);
        target.Read(context => context.Documents.Count() + context.Set<SettingRow>().Count() + context.Set<Role>().Count(item => item.Id == role)).ShouldBe(0);
    }

    [Theory]
    [InlineData(TransferPackage.Settings, """[{"area":"transfer-mail"}]""")]
    [InlineData(TransferPackage.Roles, """[{"name":"no-id","requireTwoFactor":false,"grants":[]}]""")]
    public async Task ASettingWithoutItsValueOrARoleWithoutItsIdIsRefusedBeforeAnyWrite(string path, string content)
    {
        AddRole(_source, Guid.CreateVersion7(), SiteA.Value, "content.*.read");
        await CreateAsSystemAsync(_source, "author", new ContentDocument { ["name"] = "Ada" });
        var package = Rewrite(await ExportAsync(_source, new TransferExportRequest { Roles = true }), path, content);
        var target = Target();

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, package, new TransferImportRequest { Roles = true }));

        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        target.Read(context => context.Documents.Count() + context.Set<SettingRow>().Count()).ShouldBe(0);
    }

    // ZipArchive.GetEntry reads one of two files of a name, while a list of the files has both.
    [Fact]
    public async Task APackageWithTwoFilesOfOneNameIsRefusedBeforeAnyWrite()
    {
        await SeedAsync();
        var package = Add(await ExportAsync(_source), TransferPackage.ContentPath("article"), "");
        var target = Target();

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, package));

        refused.Errors.Single().Code.ShouldBe(TransferErrorCodes.InvalidPackage);
        refused.Errors.Single().Message.ShouldContain($"two files named {TransferPackage.ContentPath("article")}");
        target.Read(context => context.Documents.Count()).ShouldBe(0);
    }

    // The hash of the package is the instance's, so its schema is not read, and the file of a type the instance does not
    // have is still refused for what it is rather than once a line.
    [Fact]
    public async Task AFileOfATypeTheInstanceDoesNotHaveIsRefusedWhenTheSchemaHashesAreEqual()
    {
        await SeedAsync();
        var package = Add(await ExportAsync(_source), TransferPackage.ContentPath("unknown"), "{}\n{}\n");
        var target = Target();

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(target, package));

        refused.Errors.ShouldHaveSingleItem().ShouldBe(new ValidationError("types.unknown", TransferErrorCodes.SchemaIncompatible, refused.Errors[0].Message));
        target.Read(context => context.Documents.Count()).ShouldBe(0);
    }

    [Fact]
    public async Task ALineWithoutAnIdOrALocaleCodeOrWithAVersionNumberOutOfRangeIsAnItemError()
    {
        var ids = await SeedAsync();
        var exported = await ExportAsync(_source);
        var lines = Entries(exported)[TransferPackage.ContentPath("author")].Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToList();
        var (ada, grace) = (ids.Single(id => id.Name == "Ada").Id, ids.Single(id => id.Name == "Grace").Id);
        var withoutId = lines.Single(line => line["id"]!.GetValue<Guid>() == ada).DeepClone().AsObject();
        withoutId.Remove("id");
        var emptyId = lines.Single(line => line["id"]!.GetValue<Guid>() == ada).DeepClone().AsObject();
        emptyId["id"] = Guid.Empty;
        var withoutCode = lines.Single(line => line["id"]!.GetValue<Guid>() == ada).DeepClone().AsObject();
        withoutCode["locales"]![0]!.AsObject().Remove("locale");
        var overflowing = lines.Single(line => line["id"]!.GetValue<Guid>() == ada);
        overflowing["locales"]![0]!["draftVersion"] = int.MaxValue;
        var zero = lines.Single(line => line["id"]!.GetValue<Guid>() == grace);
        zero["locales"]![0]!["publishedVersion"] = 0;
        var target = Target();

        var result = await ImportAsync(target, Rewrite(exported, TransferPackage.ContentPath("author"), string.Join('\n', new[] { withoutId, emptyId, withoutCode, overflowing, zero }.Select(line => line.ToJsonString()))));

        result.Sections["author"].ShouldBe(TransferCounts.None with { Errors = 5 });
        result.Items.Where(item => item.Section == "author").Select(item => (item.Id, item.Code)).ShouldBe(
        [
            ("line 1", TransferErrorCodes.InvalidItem),
            ("line 2", TransferErrorCodes.InvalidItem),
            (ada.ToString("D"), TransferErrorCodes.InvalidItem),
            (ada.ToString("D"), TransferErrorCodes.InvalidItem),
            (grace.ToString("D"), TransferErrorCodes.InvalidItem),
        ]);
        result.Items.Single(item => item.Message!.StartsWith("Line 3 ", StringComparison.Ordinal)).Message!.ShouldContain("its code");
        target.Read(context => context.Documents.Count(document => document.Type == "author")).ShouldBe(0);
    }

    // The plan refuses the locales of the manifest that the site does not allow; a line can name others.
    [Fact]
    public async Task ALocaleThatTheManifestDoesNotListRefusesItsEntryUnlessSkipped()
    {
        var ids = await SeedAsync();
        var first = ids.Single(id => id.Name == "First").Id;
        var exported = await ExportAsync(_source);
        var lines = Entries(exported)[TransferPackage.ContentPath("article")].Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToList();
        var line = lines.Single(item => item["id"]!.GetValue<Guid>() == first);
        var german = line["locales"]!.AsArray().Single(locale => locale!["locale"]!.GetValue<string>() == "en")!.DeepClone();
        german["locale"] = "de";
        line["locales"]!.AsArray().Add(german);
        var package = Rewrite(exported, TransferPackage.ContentPath("article"), string.Join('\n', lines.Select(item => item.ToJsonString())));
        JsonNode.Parse(Entries(package)[TransferPackage.Manifest])!["locales"]!.AsArray().Select(code => code!.GetValue<string>()).ShouldNotContain("de");
        var (refusing, skipping) = (Target(), Target());

        var refused = await ImportAsync(refusing, package);
        var skipped = await ImportAsync(skipping, package, new TransferImportRequest { SkipMissingLocales = true });

        var item = refused.Items.Single(item => item.Section == "article");
        (item.Id, item.Code).ShouldBe((first.ToString("D"), TransferErrorCodes.LocaleNotInSite));
        refused.Sections["article"].ShouldBe(TransferCounts.None with { Created = 1, Errors = 1 });
        refusing.Read(context => context.Documents.Count(document => document.DocumentId == first)).ShouldBe(0);
        skipped.Sections["article"].ShouldBe(TransferCounts.None with { Created = 2 });
        skipping.Read(context => context.Documents.Where(document => document.DocumentId == first).Select(document => document.Locale).OrderBy(code => code).ToList()).ShouldBe(["en", "fr", "fr-CA"]);
    }

    // The entries of a global type are every site's, so exporting them needs the instance grant that importing them needs.
    [Fact]
    public async Task ASiteAdministratorExportsTheGlobalTypesOnlyWithTheInstanceGrant()
    {
        await SeedAsync();
        var role = AddRole(_source, Guid.CreateVersion7(), SiteA.Value, CorePermissions.TransferManage);
        var siteAdmin = WithRole(role);
        var target = Target();
        AddRole(target, role, SiteA.Value, CorePermissions.TransferManage);

        var everything = await Should.ThrowAsync<ManagementAccessDeniedException>(() => ExportAsync(_source, principal: siteAdmin));
        await Should.ThrowAsync<ManagementAccessDeniedException>(() => ExportAsync(_source, new TransferExportRequest { Types = ["article", "tag"] }, siteAdmin));
        var own = await ExportAsync(_source, new TransferExportRequest { GlobalTypes = false }, siteAdmin);
        var instance = await ExportAsync(_source, new TransferExportRequest { Types = ["tag"] });
        var imported = await ImportAsync(target, own, principal: siteAdmin);

        everything.Message.ShouldContain("\"globalTypes\": false");
        Entries(own).Keys.ShouldNotContain(TransferPackage.ContentPath("tag"));
        Entries(own).Keys.ShouldContain(TransferPackage.ContentPath("article"));
        Entries(instance).Keys.ShouldContain(TransferPackage.ContentPath("tag"));
        imported.Total.Errors.ShouldBe(0);
        target.Read(context => context.Documents.Count(document => document.Type == "tag")).ShouldBe(0);
    }

    // A dry run runs every step of the import, its hooks too, so that its report is the import's: a hook refuses what it
    // refuses in the import, and a hook that acts outside the database acts in a dry run too.
    [Fact]
    public async Task ADryRunRunsTheHooksOfItsEntries()
    {
        var source = Source(NoteSchema(maxLength: null));
        await CreateAsSystemAsync(source, "note", new ContentDocument { ["title"] = "Fine" });
        var refused = await CreateAsSystemAsync(source, "note", new ContentDocument { ["title"] = "Refuse" });
        var package = await ExportAsync(source);
        var calls = new HookCalls();
        var target = Target(NoteSchema(maxLength: null), cms =>
        {
            cms.Services.AddSingleton(calls);
            cms.Services.AddScoped<IBeforeCreateHook<ContentDocument>, RefusingHook>();
            cms.Services.AddScoped<IAfterCreateHook<ContentDocument>, CountingHook>();
        });

        var dry = await ImportAsync(target, package, new TransferImportRequest { DryRun = true });

        calls.Count.ShouldBe(1);
        target.Read(context => context.Documents.Count()).ShouldBe(0);
        dry.Items.ShouldHaveSingleItem().ShouldBe(new TransferItem("note", IdOf(refused).ToString("D"), TransferOutcome.Error, RefusingHook.Code, dry.Items[0].Message));
        Report(dry).ShouldBe(Report(await ImportAsync(target, package)));
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

    // The counts of the content types and the contributors, without the settings.
    private static TransferCounts ContentCounts(TransferResult result) =>
        TransferCounts.Sum(result.Sections.Where(section => section.Key != TransferImporter.SettingsSection).Select(section => section.Value));

    private static Dictionary<string, string> Entries(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        });
    }

    private static Guid AddRole(ServiceDatabase database, Guid id, Guid site, params string[] permissions)
    {
        database.Read(context =>
        {
            context.Add(new Role { Id = id, SiteId = site, Name = $"role-{id:N}" });
            context.AddRange(permissions.Select(permission => new RoleGrant { Id = Guid.CreateVersion7(), RoleId = id, Permission = permission }));
            return context.SaveChanges();
        });
        return id;
    }

    private static byte[] Rewrite(byte[] package, string path, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var stream = new MemoryStream();
        stream.Write(package);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry(path)!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry(path, level).Open());
            writer.Write(content);
        }

        return stream.ToArray();
    }

    // One more file at the path, beside any the package has there.
    private static byte[] Add(byte[] package, string path, string content)
    {
        using var stream = new MemoryStream();
        stream.Write(package);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open());
            writer.Write(content);
        }

        return stream.ToArray();
    }

    // The file at the path as a stored entry whose central directory declares the size given, which System.IO.Compression
    // does not hold a stored entry to: it yields the whole file whatever the size declared.
    private static byte[] Craft(byte[] package, string path, uint declared)
    {
        var content = Entries(package)[path];
        content.Length.ShouldBeGreaterThan((int)declared);
        return ZipCentralDirectory.Declare(Rewrite(package, path, content, CompressionLevel.NoCompression), path, declared);
    }

    private static Task<byte[]> ExportAsync(ServiceDatabase database, TransferExportRequest? request = null, Principal? principal = null) =>
        database.AsAsync(principal ?? Admin, async services =>
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

    private ServiceDatabase Source(IInterceptor interceptor)
    {
        var source = new ServiceDatabase(cms =>
        {
            Configure(cms);
            cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(interceptor));
        });
        _targets.Add(source);
        return source;
    }

    // Runs a save, once, just before the second read of cms_document, when the export has read the ids of the entries.
    private sealed class SaveDuringExport : DbCommandInterceptor
    {
        private int _reads;

        public Func<Task>? Run { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Run is { } run && command.CommandText.Contains("cms_document", StringComparison.Ordinal) && ++_reads == 2)
            {
                Run = null;
                await run();
            }

            return result;
        }
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

    // What a hook with an effect outside the database saw.
    public sealed class HookCalls
    {
        private int _count;

        public int Count => _count;

        public void Add() => Interlocked.Increment(ref _count);
    }

    public sealed class CountingHook(HookCalls calls) : IAfterCreateHook<ContentDocument>
    {
        public ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            calls.Add();
            return ValueTask.CompletedTask;
        }
    }

    // A lock that is always free and that the test loses, as when another holder took it after the lease ran out.
    public sealed class LosingLock : IDistributedLock, IDisposable
    {
        private readonly CancellationTokenSource _lost = new();

        public void Lose() => _lost.Cancel();

        public Task<IDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDistributedLockHandle?>(new Handle(name, _lost.Token));

        public void Dispose() => _lost.Dispose();

        private sealed class Handle(string name, CancellationToken lost) : IDistributedLockHandle
        {
            public string Name => name;

            public CancellationToken Lost => lost;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // A body that runs the probe before its first byte is read.
    private sealed class ProbedStream(byte[] bytes, Func<Task> probe) : MemoryStream(bytes)
    {
        private bool _probed;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_probed)
            {
                _probed = true;
                await probe();
            }

            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
