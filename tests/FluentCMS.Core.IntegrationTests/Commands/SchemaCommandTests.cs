using System.Text;
using System.Text.Json;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Commands;

// `schema snapshot`, `validate` and `diff` inside a host, as the tool hands them on.
public sealed class SchemaCommandTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-schema-commands-");

    private string Database => Path.Combine(_directory.FullName, "cms.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _directory.Delete(recursive: true);
    }

    private static FluentCmsTestHost Host(Action<SchemaBuilder> types, string? file = null)
    {
        var host = new FluentCmsTestHost().WithServices(services => services.AddSingleton(provider =>
        {
            var builder = new SchemaBuilder(provider.GetRequiredService<IFieldTypeRegistry>());
            types(builder);
            return builder.Build();
        }));
        return file is null ? host : host.UseSqlite(file);
    }

    private static void Titled(SchemaBuilder builder) => builder.AddCollection("article", type => type.Field("title", "text"));

    private static void WithSubtitle(SchemaBuilder builder) =>
        builder.AddCollection("article", type => type.Field("title", "text").Field("subtitle", "text"));

    private string WriteFile(string name, string json)
    {
        var path = Path.Combine(_directory.FullName, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task SnapshotWritesTheCanonicalSnapshotToStandardOutputOrAFile()
    {
        await using var host = Host(Titled);
        var expected = host.Services.GetRequiredService<SchemaModel>();
        var json = SchemaSnapshot.Of(expected).Json;

        var printed = await host.RunCommandAsync("schema", "snapshot");
        var file = Path.Combine(_directory.FullName, "snapshot.json");
        var written = await host.RunCommandAsync("schema", "snapshot", "--output", file);

        printed.ExitCode.ShouldBe(0, printed.Error);
        printed.Output.ShouldBe(json);
        written.ExitCode.ShouldBe(0, written.Error);
        written.Output.ShouldContain($"Wrote {file}");
        File.ReadAllBytes(file).ShouldBe(Encoding.UTF8.GetBytes(json));
    }

    [Fact]
    public async Task ValidatePrintsEveryErrorOfABrokenHostSchemaAndExitsWithOne()
    {
        await using var host = Host(builder => builder.AddCollection("article", type => type.Field("title", "nope").Field("status", "text")));

        var result = await host.RunCommandAsync("schema", "validate");

        result.ExitCode.ShouldBe(1);
        result.Output.ShouldContain("FCMS1203: field type 'nope' used by article.title is not registered");
        result.Output.ShouldContain("FCMS1206: ");
        result.Error.ShouldStartWith("FCMS3320: the host's schema has 2 errors");
    }

    [Fact]
    public async Task ValidateSaysAValidSchemaIsValid()
    {
        await using var host = Host(Titled);

        var result = await host.RunCommandAsync("schema", "validate");

        result.ExitCode.ShouldBe(0, result.Error);
        result.Output.ShouldStartWith("The host's schema is valid: 1 type, schema hash ");
    }

    [Fact]
    public async Task ValidateChecksASchemaFileWithTheHostsFieldTypes()
    {
        await using var host = Host(Titled);
        var broken = WriteFile("broken.json", """{ "collections": [ { "name": "article", "fields": [ { "name": "title", "type": "nope" } ] } ] }""");
        // The media plugin, which the Blog sample brings, registers its field type in this host.
        var valid = WriteFile("valid.json", """{ "collections": [ { "name": "article", "fields": [ { "name": "hero", "type": "media" } ] } ] }""");

        var failed = await host.RunCommandAsync("schema", "validate", "--schema", broken);
        var passed = await host.RunCommandAsync("schema", "validate", "--schema", valid);
        var missing = await host.RunCommandAsync("schema", "validate", "--schema", Path.Combine(_directory.FullName, "missing.json"));

        failed.ExitCode.ShouldBe(1);
        failed.Output.ShouldContain("FCMS1203: field type 'nope' used by article.title is not registered");
        failed.Error.ShouldStartWith($"FCMS3320: {broken} has 1 error");
        passed.ExitCode.ShouldBe(0, passed.Error);
        passed.Output.ShouldStartWith($"{valid} is valid: 1 type");
        missing.ExitCode.ShouldBe(1);
        missing.Error.ShouldStartWith("FCMS3321: ");
    }

    [Fact]
    public async Task DiffFromTheAppliedSchemaExitsWithZeroUntilTheCodeChangesAndThenNamesTheField()
    {
        await using (var applied = Host(Titled, Database))
        {
            // A start syncs the content tables in Development and records the schema in the history.
            _ = applied.Services;

            var same = await applied.RunCommandAsync("schema", "diff", "--applied");
            same.ExitCode.ShouldBe(0, same.Error);
            same.Output.Trim().ShouldBe("No difference.");
        }

        await using var changed = Host(WithSubtitle, Database);

        var result = await changed.RunCommandAsync("schema", "diff", "--applied");

        result.ExitCode.ShouldBe(1);
        result.Output.Trim().ShouldBe("field 'article.subtitle' added");
        result.Error.ShouldStartWith("FCMS3322: 1 difference from the applied schema to the host's schema");
    }

    [Fact]
    public async Task DiffFromASchemaFileMarksWhatIsDestructiveAndSuggestsTheRename()
    {
        await using var host = Host(builder => builder.AddCollection("article", type => type.Field("title", "text").Field("summary", "text")));
        var file = WriteFile("deployed.json", """{ "collections": [ { "name": "article", "fields": [ { "name": "title", "type": "text" }, { "name": "subtitle", "type": "text" } ] } ] }""");

        var result = await host.RunCommandAsync("schema", "diff", "--schema", file, "--json");

        result.ExitCode.ShouldBe(1);
        var report = JsonDocument.Parse(result.Output).RootElement;
        report.GetProperty("changes").EnumerateArray().Select(change => (change.GetProperty("text").GetString(), change.GetProperty("destructive").GetBoolean()))
            .ShouldBe([("field 'article.summary' added", false), ("field 'article.subtitle' removed", true)], ignoreOrder: true);
        report.GetProperty("possibleRenames")[0].GetProperty("attribute").GetString().ShouldBe("[FormerNames(\"subtitle\")]");
        result.Error.ShouldStartWith($"FCMS3322: 2 differences, 1 destructive from {file} to the host's schema");
    }

    [Fact]
    public async Task DiffFromTheAppliedSchemaOfAnEmptyHistorySaysSo()
    {
        await using var host = Host(Titled, Database);
        (await host.RunCommandAsync("migrate")).ExitCode.ShouldBe(0);

        var result = await host.RunCommandAsync("schema", "diff", "--applied");

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldStartWith("FCMS3323: the schema history of the host's database is empty");
    }

    [Fact]
    public async Task DiffWithNothingToCompareIsBadUsage()
    {
        await using var host = Host(Titled);

        var result = await host.RunCommandAsync("schema", "diff");

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldStartWith("FCMS3300: 'fluentcms schema diff' compares from --applied");
    }
}
