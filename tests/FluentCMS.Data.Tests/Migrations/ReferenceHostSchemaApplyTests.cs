extern alias ReferenceImage;

using FluentCMS.Core.Commands;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Data.Tests.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using ReferenceHost = ReferenceImage::FluentCMS.Host.ReferenceHost;

namespace FluentCMS.Data.Tests.Migrations;

// The reference image on an empty database of the provider of the run, with a schema file and with the sample, as an init
// container runs it: `migrate`, then `schema apply`, then a start. MigrateOnStartup is false and SchemaSync Off, which is a
// server's default and makes SQLite take the same path.
public sealed class ReferenceHostSchemaApplyTests : IDisposable
{
    private const string SchemaFile = """{ "collections": [ { "name": "post", "fields": [ { "name": "title", "type": "text", "required": true }, { "name": "body", "type": "richText" } ] } ] }""";

    private readonly TestDatabase _database = TestDatabase.Create();

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("fluentcms-reference-host-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _database.Dispose();
        try
        {
            _root.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData(ReferenceHost.SchemaVariable)]
    [InlineData(ReferenceHost.SampleVariable)]
    public async Task MigrateThenSchemaApplyLetTheImageStart(string types)
    {
        var settings = Settings(types);

        var (exitCode, _, error) = await RunAsync(settings, "migrate");
        exitCode.ShouldBe(0, error);

        await using (var stopped = Build(settings))
        {
            var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => stopped.StartAsync(Token));
            exception.Message.ShouldStartWith("FCMS1301: ");
            exception.Message.ShouldContain("run 'fluentcms schema apply'");
        }

        (exitCode, var output, error) = await RunAsync(settings, "schema", "apply");
        exitCode.ShouldBe(0, error);
        output.ShouldContain("  create table cms_");

        (exitCode, output, error) = await RunAsync(settings, "schema", "apply");
        exitCode.ShouldBe(0, error);
        output.ShouldStartWith("Nothing to apply;");

        await using var app = Build(settings);
        await app.StartAsync(Token);
        var report = await app.Services.GetRequiredService<StartupReportFactory>().CreateAsync(Token);
        await app.StopAsync(Token);

        report.Schema.Drifted.ShouldBeFalse();
        report.Schema.AppliedHash.ShouldBe(report.Schema.Hash);
    }

    private Dictionary<string, string?> Settings(string types)
    {
        var settings = new Dictionary<string, string?>
        {
            [ReferenceHost.ProviderKey] = TestProvider.IsSqlite ? "Sqlite" : TestProvider.IsPostgreSql ? "PostgreSql" : "SqlServer",
            [$"ConnectionStrings:{ReferenceHost.ConnectionStringName}"] = _database.ConnectionString,
            ["FluentCms:Database:MigrateOnStartup"] = "false",
            ["FluentCms:Database:SchemaSync"] = "Off",
            ["FluentCms:Security:AllowGeneratedSigningKey"] = "true",
            ["FluentCms:Security:AllowUnencryptedKeyRing"] = "true",
            ["FluentCms:Security:AllowRequestOrigin"] = "true",
        };

        if (types == ReferenceHost.SchemaVariable)
        {
            var file = Path.Combine(_root.FullName, "schema.json");
            File.WriteAllText(file, SchemaFile);
            settings[ReferenceHost.SchemaVariable] = file;
        }
        else
        {
            settings[ReferenceHost.SampleVariable] = ReferenceHost.BlogSampleName;
        }

        return settings;
    }

    private WebApplication Build(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", ContentRootPath = _root.FullName });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(settings);
        return ReferenceHost.Build(builder);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(Dictionary<string, string?> settings, params string[] args)
    {
        await using var app = Build(settings);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandRunner.RunAsync(app.Services, [CommandRunner.Prefix, .. args], output, error, Token);
        return (exitCode!.Value, output.ToString(), error.ToString());
    }
}
