using System.Reflection;
using Xunit;

namespace FluentCMS.Cli.Tests;

// A run of the tool in this process, with its output captured, and a directory of the test's own that is the host's
// content root: its appsettings.json names a SQLite file there.
internal sealed class ToolRun : IDisposable
{
    public static readonly string HostProject = Path.GetFullPath(Metadata("HostProject"));
    public static readonly string HostAssembly = Path.GetFullPath(Metadata("HostAssembly"));
    public static readonly string Configuration = Metadata("Configuration");

    private readonly DirectoryInfo _directory = System.IO.Directory.CreateTempSubdirectory("fluentcms-cli-");

    public ToolRun()
    {
        WriteSettings("appsettings.json", "default.db");
    }

    public string Root => _directory.FullName;

    public StringWriter Output { get; private set; } = new();

    public StringWriter Error { get; private set; } = new();

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    // The host of this run, built and in its own folder, run with the test's directory as its project directory.
    public Task<int> HostCommandAsync(params string[] args) => RunAsync([.. args, "--assembly", HostAssembly, "--project", Root]);

    public async Task<int> RunAsync(params string[] args)
    {
        Output = new StringWriter();
        Error = new StringWriter();
        return await CommandLine.RunAsync(args, Output, Error, cancellationToken: Token);
    }

    public void WriteSettings(string file, string database) =>
        File.WriteAllText(Path.Combine(Root, file), $$"""{ "ConnectionStrings": { "FluentCms": "Data Source={{database}};Pooling=False" } }""");

    public bool Exists(string file) => File.Exists(Path.Combine(Root, file));

    public void Dispose()
    {
        Output.Dispose();
        Error.Dispose();
        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Metadata(string key) =>
        typeof(ToolRun).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == key).Value!;
}
