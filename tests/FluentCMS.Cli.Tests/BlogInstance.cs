using System.Reflection;
using FluentCMS.Cli.Generate;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Schema;
using FluentCMS.Plugins.Media;
using FluentCMS.Samples.Blog;
using FluentCMS.Schema;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCMS.Cli.Tests;

// The Blog sample on a test host: the instance that --url reaches, with a management key, and the host project whose
// commands an in-process launcher runs as the tool would start them.
public sealed class BlogInstance : IAsyncLifetime
{
    public const string Url = "http://localhost/";

    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-cli-media-");

    public BlogInstance()
    {
        Host = new FluentCmsTestHost()
            .WithContentTypes([.. BlogSample.ContentTypes])
            .WithServices(services => services.PostConfigure<MediaOptions>(options => options.Local.Root = _media.FullName));
    }

    public FluentCmsTestHost Host { get; }

    public string Token { get; private set; } = "";

    public string Snapshot => SchemaSnapshot.Of(Host.Services.GetRequiredService<SchemaModel>()).Json;

    public static string RepositoryRoot => Path.GetFullPath(typeof(BlogInstance).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "RepositoryRoot").Value!);

    public async ValueTask InitializeAsync()
    {
        var key = await Host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: TestContext.Current.CancellationToken);
        Token = key.Secret;
    }

    public HttpMessageHandler Handler() => ((TestServer)Host.Services.GetRequiredService<IServer>()).CreateHandler();

    // Runs the tool with --url pointed at this instance, or in project mode with the host's commands run in this process.
    public async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandLine.RunAsync(args, output, error, new InProcessLauncher(Host), _ => new HttpClient(Handler()), TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        _media.Delete(recursive: true);
    }

    internal static ClientSchema Schema(string snapshot) => ClientSchema.Read(snapshot, "the test");

    // Hands a command to the test host as the tool hands it to a host's process.
    private sealed class InProcessLauncher(FluentCmsTestHost host) : IHostLauncher
    {
        public async Task<int> RunAsync(HostTarget target, string? environment, IReadOnlyList<string> arguments, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        {
            var result = await host.RunCommandAsync([.. arguments]);
            await output.WriteAsync(result.Output);
            await error.WriteAsync(result.Error);
            return result.ExitCode;
        }
    }
}
