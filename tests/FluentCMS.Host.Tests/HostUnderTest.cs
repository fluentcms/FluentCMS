using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FluentCMS.Host.Tests;

// The reference host as the image runs it, on a TestServer: Production, the generated signing key accepted as the
// Dockerfile accepts it, and SQLite with no configuration in a directory of its own, which is the content root.
internal sealed class HostUnderTest : IAsyncDisposable
{
    public static readonly Uri Https = new("https://localhost/");

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-host-tests-");

    private WebApplication? _app;

    public string Root => _directory.FullName;

    public WebApplication App => _app ?? throw new InvalidOperationException("The host is not built.");

    public WebApplication Build(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null, string environment = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment, ContentRootPath = Root });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["FluentCms:Security:AllowGeneratedSigningKey"] = "true" });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        services?.Invoke(builder.Services);
        return _app = ReferenceHost.Build(builder);
    }

    public async Task<WebApplication> StartAsync(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null, string environment = "Production")
    {
        var app = Build(settings, services, environment);
        await app.StartAsync(Token);
        return app;
    }

    public HttpClient Client(Uri? baseAddress = null)
    {
        var client = App.GetTestClient();
        client.BaseAddress = baseAddress ?? Https;
        return client;
    }

    public string WriteFile(string name, string content)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path, content);
        return path;
    }

    public static async Task<(HttpStatusCode Status, JsonNode? Body)> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Token);
        return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            if (_app.Lifetime.ApplicationStarted.IsCancellationRequested)
            {
                await _app.StopAsync();
            }

            await _app.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
