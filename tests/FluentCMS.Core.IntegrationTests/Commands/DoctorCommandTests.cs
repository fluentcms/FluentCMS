using System.Text.Json;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Commands;
using FluentCMS.Core.IntegrationTests.Hosting;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Commands;

// `doctor`: the startup report of a host that is not started, and why it would not start.
public sealed class DoctorCommandTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-doctor-");
    private readonly CmsWebHost _webHost = new();

    private string Database => Path.Combine(_directory.FullName, "cms.db");

    public async ValueTask DisposeAsync()
    {
        await _webHost.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _directory.Delete(recursive: true);
    }

    [Fact]
    public async Task APendingMigrationIsNamedAndExitsWithOneAndNothingIsApplied()
    {
        await using var host = new FluentCmsTestHost().UseSqlite(Database);

        var result = await host.RunCommandAsync("doctor");

        result.ExitCode.ShouldBe(1);
        result.Output.ShouldContain("Pending migrations:");
        result.Output.ShouldContain($"{Environment.NewLine}  system  ");
        result.Error.ShouldContain("FCMS3303: ");
        result.Error.ShouldContain("pending; run 'fluentcms migrate' to apply them");

        Tables(Database).ShouldBeEmpty();
        var check = await host.RunCommandAsync("migrate", "--check");
        check.ExitCode.ShouldBe(1);
    }

    [Fact]
    public async Task AMigratedHostIsHealthyAndTheReportNamesItsParts()
    {
        await using var host = new FluentCmsTestHost().UseSqlite(Database);
        _ = host.Services;

        var result = await host.RunCommandAsync("doctor");

        result.ExitCode.ShouldBe(0, result.Error);
        result.Output.ShouldStartWith("FluentCMS ");
        result.Output.ShouldContain(" in Development");
        result.Output.ShouldContain("Data provider: SQLite ");
        result.Output.ShouldContain("  media ");
        result.Output.ShouldContain($"Pending migrations:{Environment.NewLine}  none");
        result.Output.ShouldContain("development-email-sender: ");
        result.Error.ShouldNotContain("FCMS3326");
    }

    [Fact]
    public async Task AProductionRefusalIsNamedWithoutStartingTheHostOrTouchingTheDatabase()
    {
        var app = _webHost.Build(Environments.Production, new() { ["FluentCms:Security:AllowRequestOrigin"] = "false" });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CommandRunner.RunAsync(app.Services, ["fluentcms", "doctor"], output, error, Token);

        exitCode.ShouldBe(1);
        error.ToString().ShouldContain("FCMS3325: the host would not start: FCMS1105: ");
        output.ToString().ShouldContain(" in Production");
        app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested.ShouldBeFalse();
        Tables(_webHost.DatabaseFile).ShouldBeEmpty();
    }

    [Fact]
    public async Task ABrokenSchemaIsARefusal()
    {
        await using var host = new FluentCmsTestHost().WithServices(services => services.AddSingleton(_ =>
            new Core.Schema.SchemaBuilder().AddCollection("article", type => type.Field("title", "nope")).Build()));

        var result = await host.RunCommandAsync("doctor");

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldStartWith("FCMS3325: the host would not start: The schema has 1 error:");
        result.Error.ShouldContain("FCMS1203");
    }

    [Fact]
    public async Task TheJsonHasTheShapeOfTheDiagnosticsEndpoint()
    {
        await using var host = new FluentCmsTestHost().UseSqlite(Database);
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var client = host.AsKey(key);
        var served = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token)).RootElement;

        var result = await host.RunCommandAsync("doctor", "--json");

        result.ExitCode.ShouldBe(0, result.Error);
        result.Output.Trim().ShouldNotContain("\n");
        var printed = JsonDocument.Parse(result.Output).RootElement;
        Shape(printed).ShouldBe(Shape(served));
        printed.GetProperty("schema").GetProperty("hash").GetString().ShouldBe(served.GetProperty("schema").GetProperty("hash").GetString());
    }

    // The property names, nested ones by their path, of the objects of a JSON value.
    private static List<string> Shape(JsonElement element, string path = "")
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return [.. element.EnumerateObject()
            .SelectMany(property => (IEnumerable<string>)[$"{path}{property.Name}", .. Shape(property.Value, $"{path}{property.Name}.")])
            .Order(StringComparer.Ordinal)];
    }

    private static List<string> Tables(string file)
    {
        if (!File.Exists(file))
        {
            return [];
        }

        using var connection = new SqliteConnection($"Data Source={file};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "select name from sqlite_master where type = 'table'";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
