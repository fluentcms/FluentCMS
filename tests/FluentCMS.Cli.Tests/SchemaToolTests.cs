using System.Text.Json;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Cli.Tests;

// The schema commands and doctor against an instance of --url, which is a test host here, and the host's side of them,
// which a launcher runs in that test host or another.
public sealed class SchemaToolTests : IAsyncDisposable
{
    private const string Instance = "http://localhost";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-schema-tool-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<FluentCmsTestHost> _hosts = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        _output.Dispose();
        _error.Dispose();
        _directory.Delete(recursive: true);
    }

    private FluentCmsTestHost Host(params string[] fields)
    {
        var host = new FluentCmsTestHost().WithServices(services => services.AddSingleton(provider =>
            new SchemaBuilder(provider.GetRequiredService<IFieldTypeRegistry>())
                .AddCollection("article", type =>
                {
                    foreach (var field in fields)
                    {
                        type.Field(field, "text");
                    }
                })
                .Build()));
        _hosts.Add(host);
        return host;
    }

    private static async Task<string> KeyAsync(FluentCmsTestHost host, string role = BuiltInRoles.SuperAdminName) =>
        (await host.CreateApiKeyAsync(ApiKeyClass.Management, role, cancellationToken: Token)).Secret;

    private string PathOf(string file) => Path.Combine(_directory.FullName, file);

    // The tool with the instance on the test server and the project's host run in the test host given.
    private Task<int> RunAsync(FluentCmsTestHost instance, FluentCmsTestHost? project, params string[] args)
    {
        _output.GetStringBuilder().Clear();
        _error.GetStringBuilder().Clear();
        return CommandLine.RunAsync(
            [.. args, "--assembly", ToolRun.HostAssembly, "--project", _directory.FullName],
            _output,
            _error,
            new TestHostLauncher(project ?? instance),
            _ => instance.CreateClient(),
            Token);
    }

    [Fact]
    public async Task SnapshotInTheProjectAndWithUrlWriteTheSameBytes()
    {
        var host = Host("title", "subtitle");
        var key = await KeyAsync(host);

        (await RunAsync(host, null, "schema", "snapshot", "--output", PathOf("project.json"))).ShouldBe(0, _error.ToString());
        (await RunAsync(host, null, "schema", "snapshot", "--output", PathOf("instance.json"), "--url", Instance, "--token", key)).ShouldBe(0, _error.ToString());

        File.ReadAllBytes(PathOf("instance.json")).ShouldBe(File.ReadAllBytes(PathOf("project.json")));
        _output.ToString().ShouldContain($"Wrote {PathOf("instance.json")}");
        _output.ToString().ShouldNotContain(key);
    }

    [Fact]
    public async Task DiffWithUrlExitsWithZeroOnTheSameSchemaAndWithOneAfterAFieldIsAddedAndNamesIt()
    {
        var instance = Host("title");
        var key = await KeyAsync(instance);

        (await RunAsync(instance, Host("title"), "schema", "diff", "--url", Instance, "--token", key)).ShouldBe(0, _error.ToString());
        _output.ToString().Trim().ShouldBe("No difference.");

        (await RunAsync(instance, Host("title", "subtitle"), "schema", "diff", "--url", Instance, "--token", key)).ShouldBe(1);
        _output.ToString().Trim().ShouldBe("field 'article.subtitle' added");
        _error.ToString().ShouldStartWith($"FCMS3322: 1 difference from the instance at {Instance}/ to the host's schema");
    }

    [Fact]
    public async Task DiffWithUrlComparesASchemaFileWithoutAHostAndMarksWhatIsDestructive()
    {
        var instance = Host("title", "subtitle");
        var key = await KeyAsync(instance);
        File.WriteAllText(PathOf("schema.json"), """{ "collections": [ { "name": "article", "fields": [ { "name": "title", "type": "text" }, { "name": "summary", "type": "text" } ] } ] }""");

        (await RunAsync(instance, null, "schema", "diff", "--url", Instance, "--token", key, "--schema", PathOf("schema.json"), "--json")).ShouldBe(1);

        var report = JsonDocument.Parse(_output.ToString()).RootElement;
        report.GetProperty("changes").EnumerateArray().Single(change => change.GetProperty("destructive").GetBoolean()).GetProperty("text").GetString()
            .ShouldBe("field 'article.subtitle' removed");
        report.GetProperty("possibleRenames")[0].GetProperty("attribute").GetString().ShouldBe("[FormerNames(\"subtitle\")]");
    }

    [Fact]
    public async Task AKeyWithoutSchemaReadIsRefusedWithThePermissionNamed()
    {
        var host = Host("title");
        await host.CreateRoleAsync("nothing", [], Token);
        var key = await KeyAsync(host, "nothing");

        (await RunAsync(host, null, "schema", "snapshot", "--url", Instance, "--token", key)).ShouldBe(1);

        _error.ToString().ShouldStartWith($"FCMS3330: {Instance}/ refused the key (403); schema/snapshot needs a management key with schema.read");
        _error.ToString().ShouldNotContain(key);
    }

    [Fact]
    public async Task DoctorWithUrlPrintsTheDiagnosticsOfTheInstance()
    {
        var host = Host("title");
        var key = await KeyAsync(host);

        (await RunAsync(host, null, "doctor", "--url", Instance, "--token", key)).ShouldBe(0, _error.ToString());
        _output.ToString().ShouldStartWith("FluentCMS ");
        _output.ToString().ShouldContain($"Pending migrations:{Environment.NewLine}  none");

        (await RunAsync(host, null, "doctor", "--url", Instance, "--token", key, "--json")).ShouldBe(0, _error.ToString());
        JsonDocument.Parse(_output.ToString()).RootElement.GetProperty("schema").GetProperty("hash").GetString()
            .ShouldBe(SchemaSnapshot.Of(host.Services.GetRequiredService<SchemaModel>()).Hash);
    }

    [Fact]
    public async Task ValidateChecksASchemaFileWithoutAHost()
    {
        var host = Host("title");
        File.WriteAllText(PathOf("broken.json"), """{ "collections": [ { "name": "article", "fields": [ { "name": "status", "type": "text" } ] } ] }""");
        File.WriteAllText(PathOf("media.json"), """{ "collections": [ { "name": "article", "fields": [ { "name": "hero", "type": "media" } ] } ] }""");
        var launcher = new TestHostLauncher(host);

        (await CommandLine.RunAsync(["schema", "validate", "--schema", PathOf("broken.json")], _output, _error, launcher, Token)).ShouldBe(1);
        _output.ToString().ShouldContain("FCMS1206: ");
        _error.ToString().ShouldStartWith($"FCMS3320: {PathOf("broken.json")} has 1 error");

        _output.GetStringBuilder().Clear();
        _error.GetStringBuilder().Clear();
        (await CommandLine.RunAsync(["schema", "validate", "--schema", PathOf("media.json")], _output, _error, launcher, Token)).ShouldBe(0, _error.ToString());
        _output.ToString().ShouldStartWith($"{PathOf("media.json")} is valid: 1 type");
        _error.ToString().ShouldStartWith("note: field type 'media' is not one of core's");

        launcher.Calls.ShouldBe(0);
    }

    // Runs what the tool hands on in a test host, as the host's own process would.
    private sealed class TestHostLauncher(FluentCmsTestHost host) : IHostLauncher
    {
        public int Calls { get; private set; }

        public async Task<int> RunAsync(HostTarget target, string? environment, IReadOnlyList<string> arguments, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        {
            Calls++;
            var result = await host.RunCommandAsync([.. arguments]);
            await output.WriteAsync(result.Output);
            await error.WriteAsync(result.Error);
            return result.ExitCode;
        }
    }
}
