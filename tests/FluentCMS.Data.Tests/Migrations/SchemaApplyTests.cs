using System.Reflection;
using System.Text.RegularExpressions;
using FluentCMS.Core.Commands;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// `fluentcms schema apply` on the provider of the run, on hosts with no content migrations that are built and never
// started, as in a process that runs the command. MigrateOnStartup is false, which is the default on a server.
public sealed partial class SchemaApplyTests : IDisposable
{
    private const string Production = "Production";

    private static readonly Dictionary<string, string?> NoMigrationOnStartup = new() { ["FluentCms:Database:MigrateOnStartup"] = "false" };

    private readonly TestDatabase _database = TestDatabase.Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AFirstRunCreatesTheTablesAndASecondRunChangesNothing()
    {
        await MigrateAsync(ArticleSchemas.Titled);

        var (exitCode, output, error) = await RunAsync(ArticleSchemas.Titled, "schema", "apply");

        exitCode.ShouldBe(0, error);
        output.ShouldContain($"Operations:{Environment.NewLine}  create table cms_article{Environment.NewLine}");
        output.ShouldContain("Applied ");
        _database.Tables().ShouldContain("cms_article");
        History().ShouldHaveSingleItem();

        (exitCode, output, error) = await RunAsync(ArticleSchemas.Titled, "schema", "apply");

        exitCode.ShouldBe(0, error);
        output.ShouldStartWith("Nothing to apply; the content tables match schema ");
        History().ShouldHaveSingleItem();
        await StartWithoutDriftAsync(ArticleSchemas.Titled);
    }

    [Fact]
    public async Task AnAddedFieldStopsTheHostWithTheCommandToRunUntilSchemaApplyAddsIt()
    {
        await MigrateAndApplyAsync(ArticleSchemas.Titled);
        InsertArticle("Kept");

        using (var stopped = Host(ArticleSchemas.WithSubtitle))
        {
            var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => stopped.StartAsync(Token));
            exception.Message.ShouldBe("FCMS1301: table cms_article is missing column subtitle; run 'fluentcms schema apply', or 'fluentcms migrations add AddSubtitle' to give the host content migrations");
        }

        var (exitCode, output, error) = await RunAsync(ArticleSchemas.WithSubtitle, "schema", "apply");

        exitCode.ShouldBe(0, error);
        output.ShouldContain($"Changes from the applied schema:{Environment.NewLine}field 'article.subtitle' added{Environment.NewLine}");
        output.ShouldContain("  add column subtitle to table cms_article");
        _database.Columns("cms_article").ShouldContain("subtitle");
        _database.ReadStrings("select title from cms_article").ShouldBe(["Kept"]);
        History().Count.ShouldBe(2);
        await StartWithoutDriftAsync(ArticleSchemas.WithSubtitle);
    }

    [Fact]
    public async Task AFieldWithFormerNamesKeepsItsValuesUnderTheNewName()
    {
        await MigrateAndApplyAsync(ArticleSchemas.Article("summary"));
        InsertArticle("Kept", column: "summary");

        var renamed = SampleSchema.NewBuilder()
            .AddCollection("article", type => type.Field("abstract", "text", field => field.FormerNames("summary")))
            .Build();
        var (exitCode, output, error) = await RunAsync(renamed, "schema", "apply");

        exitCode.ShouldBe(0, error);
        output.ShouldContain("field 'article.summary' renamed to 'abstract'");
        output.ShouldContain("  rename column summary to abstract on table cms_article");
        output.ShouldNotContain("[destructive]");
        _database.Columns("cms_article").ShouldContain("abstract");
        _database.Columns("cms_article").ShouldNotContain("summary");
        _database.ReadStrings("select abstract from cms_article").ShouldBe(["Kept"]);
        (await RunAsync(renamed, "schema", "apply")).Output.ShouldStartWith("Nothing to apply;");
    }

    // A component kept in columns only has its columns renamed. One that is also kept in JSON keeps its values there under
    // the former name, so the rename counts as lost.
    [Fact]
    public async Task AComponentFieldRenamedRenamesItsColumnAndNeedsAllowDestructiveWhenTheComponentIsAlsoInJson()
    {
        await MigrateAndApplyAsync(WithSeo(field => field.Field("metaTitle", "text")));
        InsertArticle("Kept", column: "seo_meta_title");

        var renamed = WithSeo(field => field.Field("pageTitle", "text", title => title.FormerNames("metaTitle")));
        var (exitCode, output, error) = await RunAsync(renamed, "schema", "apply");

        exitCode.ShouldBe(0, error);
        output.ShouldContain("  rename column seo_meta_title to seo_page_title on table cms_article");
        output.ShouldNotContain("[destructive]");
        _database.ReadStrings("select seo_page_title from cms_article").ShouldBe(["Kept"]);

        var listed = WithSeo(field => field.Field("heading", "text", title => title.FormerNames("pageTitle")), repeated: true);
        (exitCode, _, error) = await RunAsync(listed, "schema", "apply");

        exitCode.ShouldBe(1);
        error.ShouldStartWith("FCMS3337: the schema change loses data: field 'component seo.pageTitle' renamed to 'heading', whose values are kept in JSON under 'pageTitle', which schema apply does not rename");
    }

    // Only SQL Server's reader reports check constraints, and only there does a check keep the name it was made with.
    [Fact]
    public async Task ARenamedJsonFieldKeepsItsValuesAndItsCheckTakesTheNewName()
    {
        await MigrateAndApplyAsync(SampleSchema.NewBuilder().AddCollection("article", type => type.Field("title", "text").Field("data", "json")).Build());
        InsertArticle("{}", column: "data");

        var renamed = SampleSchema.NewBuilder().AddCollection("article", type => type.Field("title", "text").Field("payload", "json", field => field.FormerNames("data"))).Build();
        var (exitCode, output, error) = await RunAsync(renamed, "schema", "apply");

        exitCode.ShouldBe(0, error);
        output.ShouldContain("  rename column data to payload on table cms_article");
        _database.ReadStrings("select payload from cms_article").ShouldBe(["{}"]);
        (await RunAsync(renamed, "schema", "apply")).Output.ShouldStartWith("Nothing to apply;");

        using var host = Host(renamed);
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        await context.Database.OpenConnectionAsync(Token);
        var database = await scope.ServiceProvider.GetRequiredService<IDataProvider>().SchemaReader.ReadAsync(context.Database.GetDbConnection(), Token);
        database.Tables["cms_article"].CheckConstraints.Select(check => check.Name).ShouldBe(TestProvider.IsSqlServer ? ["ck_cms_article_payload_json"] : []);
    }

    [Fact]
    public async Task TheReferencesOfARenamedRelationMoveToItsPathAndASecondRunFindsNothing()
    {
        await MigrateAndApplyAsync(Authored("writer"));
        _database.Execute($"insert into cms_reference (id, source_type, source_document_id, state, locale, field, position, target_type, target_id) values ('{Guid.CreateVersion7()}', 'article', '{Guid.CreateVersion7()}', 0, '', 'writer', 0, 'author', '{Guid.CreateVersion7()}')");

        var renamed = Authored("author", former: "writer");
        var (exitCode, output, error) = await RunAsync(renamed, "schema", "apply");

        exitCode.ShouldBe(0, error);
        output.ShouldContain("field 'article.writer' renamed to 'author'");
        output.ShouldContain("  rename field article.writer to article.author in table cms_reference");
        output.ShouldNotContain("[destructive]");
        _database.ReadStrings("select field from cms_reference").ShouldBe(["author"]);
        (await RunAsync(renamed, "schema", "apply")).Output.ShouldStartWith("Nothing to apply;");
    }

    [Fact]
    public async Task ARemovedFieldIsRefusedAndListedAndAppliedWithAllowDestructive()
    {
        await MigrateAndApplyAsync(ArticleSchemas.Article("title", "legacy"));
        InsertArticle("Kept", column: "legacy");

        var (exitCode, output, error) = await RunAsync(ArticleSchemas.Titled, "schema", "apply");

        exitCode.ShouldBe(1);
        output.ShouldContain("  drop column legacy from table cms_article [destructive]");
        error.ShouldStartWith("FCMS3337: the schema change loses data: drop column legacy from table cms_article; field 'article.legacy' removed; nothing was applied. Pass --allow-destructive to apply it.");
        _database.ReadStrings("select legacy from cms_article").ShouldBe(["Kept"]);
        History().ShouldHaveSingleItem();

        (exitCode, _, error) = await RunAsync(ArticleSchemas.Titled, "schema", "apply", "--allow-destructive");

        exitCode.ShouldBe(0, error);
        error.ShouldStartWith("FCMS3337: warning: the schema change loses data: drop column legacy from table cms_article");
        _database.Columns("cms_article").ShouldNotContain("legacy");
        History().Count.ShouldBe(2);
        await StartWithoutDriftAsync(ArticleSchemas.Titled);
    }

    // A JSON column has a check constraint on SQLite, which SQLite adds by rebuilding the table, and on SQL Server, which
    // keeps the column until the constraint is dropped. The rows a table has get the field's empty value.
    [Fact]
    public async Task ARequiredJsonFieldIsAddedWithItsEmptyValueAndDroppedWithItsConstraint()
    {
        await MigrateAndApplyAsync(ArticleSchemas.Titled);
        InsertArticle("Kept");

        var withBody = SampleSchema.NewBuilder()
            .AddCollection("article", type => type.Field("title", "text").Field("body", "richText"))
            .Build();
        var (exitCode, output, error) = await RunAsync(withBody, "schema", "apply");

        exitCode.ShouldBe(0, error);
        output.ShouldContain("  add column body to table cms_article, empty in the rows it has");
        _database.ReadStrings("select title from cms_article").ShouldBe(["Kept"]);
        _database.ReadStrings("select body from cms_article").ShouldHaveSingleItem().ShouldContain("paragraph");
        (await RunAsync(withBody, "schema", "apply")).Output.ShouldStartWith("Nothing to apply;");

        (exitCode, _, error) = await RunAsync(ArticleSchemas.Titled, "schema", "apply", "--allow-destructive");

        exitCode.ShouldBe(0, error);
        _database.Columns("cms_article").ShouldNotContain("body");
        _database.ReadStrings("select title from cms_article").ShouldBe(["Kept"]);
        (await RunAsync(ArticleSchemas.Titled, "schema", "apply")).Output.ShouldStartWith("Nothing to apply;");
    }

    // A wider column is altered, a removed type's table dropped, and an index the schema no longer has dropped, which
    // holds no data and so needs no flag.
    [Fact]
    public async Task AChangedColumnAndARemovedTypeNeedAllowDestructiveAndARemovedIndexDoesNot()
    {
        var before = SampleSchema.NewBuilder()
            .AddCollection("article", type => type
                .Field("title", "text", field => field.Length(null, 100))
                .Field("code", "text", field => field.Length(null, 20).Indexed()))
            .AddCollection("tag", type => type.Field("name", "text"))
            .Build();
        await MigrateAndApplyAsync(before);
        InsertArticle("Kept");

        var unindexed = SampleSchema.NewBuilder()
            .AddCollection("article", type => type
                .Field("title", "text", field => field.Length(null, 100))
                .Field("code", "text", field => field.Length(null, 20)))
            .AddCollection("tag", type => type.Field("name", "text"))
            .Build();
        var (exitCode, output, error) = await RunAsync(unindexed, "schema", "apply");

        exitCode.ShouldBe(0, error);
        output.ShouldContain("  drop index ix_cms_article_code from table cms_article");

        var after = SampleSchema.NewBuilder()
            .AddCollection("article", type => type
                .Field("title", "text", field => field.Length(null, 200))
                .Field("code", "text", field => field.Length(null, 20)))
            .Build();
        (exitCode, output, error) = await RunAsync(after, "schema", "apply");

        exitCode.ShouldBe(1);
        output.ShouldContain("  drop table cms_tag [destructive]");
        if (!TestProvider.IsSqlite)
        {
            // SQLite's TEXT has no length.
            output.ShouldContain("  change column title on table cms_article from ");
        }

        error.ShouldStartWith("FCMS3337: ");
        _database.Tables().ShouldContain("cms_tag");

        (exitCode, _, error) = await RunAsync(after, "schema", "apply", "--allow-destructive");

        exitCode.ShouldBe(0, error);
        _database.Tables().ShouldNotContain("cms_tag");
        _database.ReadStrings("select title from cms_article").ShouldBe(["Kept"]);
        (await RunAsync(after, "schema", "apply")).Output.ShouldStartWith("Nothing to apply;");
    }

    [Fact]
    public async Task DryRunAndScriptChangeNothingAndTheScriptAppliesOnTheDatabaseItWasWrittenFor()
    {
        await MigrateAndApplyAsync(ArticleSchemas.Titled);
        var file = Path.Combine(Path.GetTempPath(), $"fluentcms-schema-apply-{Guid.NewGuid():N}.sql");

        try
        {
            var (exitCode, output, error) = await RunAsync(ArticleSchemas.WithSubtitle, "schema", "apply", "--dry-run");

            exitCode.ShouldBe(0, error);
            output.ShouldContain("  add column subtitle to table cms_article");
            output.ShouldEndWith($"Dry run; nothing was applied.{Environment.NewLine}");

            (exitCode, output, error) = await RunAsync(ArticleSchemas.WithSubtitle, "schema", "apply", "--script", "--output", file);

            exitCode.ShouldBe(0, error);
            output.ShouldBe($"Wrote {file}{Environment.NewLine}");
            _database.Columns("cms_article").ShouldNotContain("subtitle");
            History().ShouldHaveSingleItem();

            var script = await File.ReadAllTextAsync(file, Token);
            script.ShouldContain("-- add column subtitle to table cms_article");
            ApplyScript(script);
            _database.Columns("cms_article").ShouldContain("subtitle");

            // The history row is the next run's, which finds nothing to do.
            (exitCode, output, error) = await RunAsync(ArticleSchemas.WithSubtitle, "schema", "apply");

            exitCode.ShouldBe(0, error);
            output.ShouldContain("Nothing to apply;");
            History().Count.ShouldBe(2);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task TwoRunsStartedTogetherApplyEachOperationOnce()
    {
        await MigrateAsync(ArticleSchemas.Titled);
        using var first = Host(ArticleSchemas.Titled);
        using var second = Host(ArticleSchemas.Titled);

        var runs = await Task.WhenAll(RunAsync(first, "schema", "apply"), RunAsync(second, "schema", "apply"));

        runs.ShouldAllBe(run => run.ExitCode == 0, string.Join(Environment.NewLine, runs.Select(run => run.Error)));
        runs.Count(run => run.Output.Contains("  create table cms_article", StringComparison.Ordinal)).ShouldBe(1);
        runs.Count(run => run.Output.StartsWith("Nothing to apply;", StringComparison.Ordinal)).ShouldBe(1);
        History().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task TheOperationsOfARunAreOneTransaction()
    {
        var titled = SampleSchema.NewBuilder()
            .AddCollection("article", type => type.Field("title", "text", field => field.Length(null, 100)))
            .Build();
        await MigrateAndApplyAsync(titled);
        InsertArticle("Same", published: true);
        InsertArticle("Same", published: true);

        // The unique index fails on the two rows, after the column is added.
        var unique = SampleSchema.NewBuilder()
            .AddCollection("article", type => type
                .Field("title", "text", field => field.Length(null, 100).Unique())
                .Field("subtitle", "text"))
            .Build();
        var (exitCode, output, error) = await RunAsync(unique, "schema", "apply");

        exitCode.ShouldBe(1);
        output.ShouldContain("  add column subtitle to table cms_article");
        error.ShouldStartWith("FCMS3339: the database refused an operation (");
        _database.Columns("cms_article").ShouldNotContain("subtitle");
        History().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AHostWithContentMigrationsIsRefused()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        await MigrateAsync(ArticleSchemas.Titled, migrations);

        using var host = Host(ArticleSchemas.WithSubtitle, migrations);
        var (exitCode, _, error) = await RunAsync(host, "schema", "apply");

        exitCode.ShouldBe(1);
        error.ShouldStartWith("FCMS3335: this host has content migrations (");
        error.ShouldContain("_Initial), so its content tables follow them");
        _database.Columns("cms_article").ShouldNotContain("subtitle");
    }

    [Fact]
    public async Task PendingSystemMigrationsAreRefused()
    {
        var (exitCode, _, error) = await RunAsync(ArticleSchemas.Titled, "schema", "apply");

        exitCode.ShouldBe(1);
        error.ShouldStartWith("FCMS3336: stream system has not applied ");
        error.ShouldContain("run 'fluentcms migrate' first");
        _database.Tables().ShouldNotContain("cms_article");
    }

    [Theory]
    [InlineData(new[] { "--output", "schema.sql" }, "FCMS3300: --output names the file of --script, so it needs --script")]
    [InlineData(new[] { "--script", "--dry-run" }, "FCMS3300: --script and --dry-run both apply nothing; give one")]
    public async Task BadUsageExitsWithTwo(string[] options, string message)
    {
        var (exitCode, _, error) = await RunAsync(ArticleSchemas.Titled, ["schema", "apply", .. options]);

        exitCode.ShouldBe(2);
        error.ShouldStartWith(message);
        error.ShouldContain("Usage: fluentcms schema apply");
    }

    private static SchemaModel WithSeo(Action<ITypeBuilder> seo, bool repeated = false) =>
        SampleSchema.NewBuilder()
            .AddComponent("seo", seo)
            .AddCollection("article", type =>
            {
                type.Field("title", "text").Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo"));
                if (repeated)
                {
                    type.Field("variants", "component", field => field.Multiple().Setting(FieldSettings.Component, "seo"));
                }
            })
            .Build();

    private static SchemaModel Authored(string field, string? former = null) =>
        SampleSchema.NewBuilder()
            .AddCollection("author", type => type.Field("name", "text"))
            .AddCollection("article", type => type
                .Field("title", "text")
                .Field(field, "relation", relation => relation.Relation("author").FormerNames(former is null ? [] : [former])))
            .Build();

    private IHost Host(SchemaModel schema, Assembly? migrations = null) =>
        _database.CreateHost(schema, Production, migrations, NoMigrationOnStartup);

    private async Task MigrateAsync(SchemaModel schema, Assembly? migrations = null)
    {
        using var host = Host(schema, migrations);
        var (exitCode, _, error) = await RunAsync(host, "migrate");
        exitCode.ShouldBe(0, error);
    }

    private async Task MigrateAndApplyAsync(SchemaModel schema)
    {
        await MigrateAsync(schema);
        var (exitCode, _, error) = await RunAsync(schema, "schema", "apply");
        exitCode.ShouldBe(0, error);
    }

    // The startup report of a host that starts after schema apply shows the applied schema as its own.
    private async Task StartWithoutDriftAsync(SchemaModel schema)
    {
        using var host = Host(schema);
        await host.StartAsync(Token);
        var report = await host.Services.GetRequiredService<StartupReportFactory>().CreateAsync(Token);
        await host.StopAsync(Token);

        report.Schema.Drifted.ShouldBeFalse();
        report.Schema.AppliedHash.ShouldBe(report.Schema.Hash);
    }

    private List<string> History() => _database.ReadStrings("select hash from cms_schema_history");

    private void InsertArticle(string value, string column = "title", bool published = false) =>
        _database.Execute($"insert into cms_article (id, document_id, site_id, state, {column}) values ('{Guid.CreateVersion7()}', '{Guid.CreateVersion7()}', '{Guid.Empty}', {(published ? 1 : 0)}, '{value}')");

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(SchemaModel schema, params string[] args)
    {
        using var host = Host(schema);
        return await RunAsync(host, args);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(IHost host, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandRunner.RunAsync(host.Services, [CommandRunner.Prefix, .. args], output, error, Token);
        return (exitCode!.Value, output.ToString(), error.ToString());
    }

    // A SQL Server script separates its batches with GO, which only tools understand. One connection, as a tool runs a
    // script, so that the transaction the script begins is the one it commits.
    private void ApplyScript(string script) =>
        _database.ExecuteBatches(TestProvider.IsSqlServer ? GoSeparator().Split(script) : [script]);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline)]
    private static partial Regex GoSeparator();
}
