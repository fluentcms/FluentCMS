using System.Net;

namespace FluentCMS.Admin.EndToEnd;

// The reference host published with no build, as the Dockerfile publishes it, and run as "dotnet FluentCMS.Host.dll" in a
// process of its own, with its data in the run's folder and the type of landing.schema.json beside the tests.
internal sealed class PublishedHost(DirectoryInfo root) : HostProcess
{
    // FLUENTCMS_E2E_PUBLISHED names a folder the publish is kept in and taken from by a later run, as the retry of the CI job's
    // failed cases does; without it the publish is the run's own and goes with it.
    public const string PublishedVariable = "FLUENTCMS_E2E_PUBLISHED";

    // Under the load of the Windows full run, beside every other test app, the publish has taken from 6 to more than 10
    // minutes. It is bounded below the job's --timeout of 25 minutes, which counts from the start of this test app, with the
    // few minutes the host's start and the tests take on Windows, where the browser tests are skipped.
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(20);

    private const string PublishedMark = "published.ok";

    private string PublishDirectory => Environment.GetEnvironmentVariable(PublishedVariable) is { Length: > 0 } kept ? kept : Path.Combine(root.FullName, "host");

    private string DataDirectory => Path.Combine(root.FullName, "data");

    private string HostAssembly => Path.Combine(PublishDirectory, "FluentCMS.Host.dll");

    public override string CertificateDirectory => DataDirectory;

    public override IPAddress ServerAddress => IPAddress.Loopback;

    public override string ServerHost => IPAddress.Loopback.ToString();

    public override string SchemaPath => Path.Combine(AppContext.BaseDirectory, "landing.schema.json");

    public override async Task<string?> PrepareAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DataDirectory);
        await PublishAsync(cancellationToken);
        return null;
    }

    public override string DataPath(string name) => Path.Combine(DataDirectory, name);

    public override string CertificatePath(string path) => path;

    public override string ListenUrl(Uri origin) => origin.GetLeftPart(UriPartial.Authority);

    public override Task<(int ExitCode, string Output, string Tail)> CommandAsync(
        IReadOnlyList<string> command, IReadOnlyDictionary<string, string?> environment, string? input, TimeSpan timeout, bool secretOutput, CancellationToken cancellationToken) =>
        ChildProcess.RunAsync("dotnet", ["exec", HostAssembly, "fluentcms", .. command], PublishDirectory, environment, timeout, input, cancellationToken, secretOutput);

    public override void Start(Uri origin, IReadOnlyDictionary<string, string?> environment) =>
        Host = ChildProcess.Start("dotnet", ["exec", HostAssembly], PublishDirectory, environment);

    // As the Dockerfile publishes it, with the SDK alone: where the wasm-tools workload is installed, as on the Windows
    // runners, the publish would otherwise relink the WebAssembly runtime, which no image does.
    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        var mark = Path.Combine(PublishDirectory, PublishedMark);
        if (File.Exists(mark))
        {
            return;
        }

        // A publish with no build takes the satellite assemblies the build made, so it is given the build's pseudo-cultures.
        string[] properties = ["-p:UseAppHost=false", "-p:WasmBuildNative=false", .. Repository.PseudoCultures ? ["-p:FluentCmsAdminPseudoCultures=true"] : Array.Empty<string>()];
        var (exitCode, _, tail) = await ChildProcess.RunAsync(
            "dotnet",
            ["publish", Repository.HostProject, "-c", Repository.Configuration, "--no-build", "-o", PublishDirectory, .. properties, "-nologo", "-nodeReuse:false"],
            Repository.Root,
            new Dictionary<string, string?> { ["MSBUILDDISABLENODEREUSE"] = "1" },
            PublishTimeout,
            input: null,
            cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Publishing the reference host ({Repository.Configuration}, with no build) exited with {exitCode}; build the solution in that configuration first.{Environment.NewLine}{tail}");
        }

        // Written last, so that a publish cut short is not taken by a later run.
        await File.WriteAllTextAsync(mark, Repository.Configuration, cancellationToken);
    }
}
