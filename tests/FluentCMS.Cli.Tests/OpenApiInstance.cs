using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using FluentCMS.Data.Sqlite;
using FluentCMS.Samples.Blog;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Cli.Tests;

// A Development host with the Blog sample that maps /openapi/v1.json as a host's Program.cs does; the test host does not.
internal sealed class OpenApiInstance : IAsyncDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-openapi-");
    private readonly WebApplication _app;

    public OpenApiInstance()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FluentCms:Security:AllowRequestOrigin"] = "true",
            ["FluentCms:Security:AllowUnencryptedKeyRing"] = "true",
            ["FluentCms:Media:Local:Root"] = Path.Combine(_directory.FullName, "media"),
        });
        builder.Services.AddFluentCms(cms => cms.UseSqlite($"Data Source={Path.Combine(_directory.FullName, "cms.db")};Pooling=False"));
        builder.Services.AddSingleton(services =>
        {
            var schema = new SchemaBuilder(services.GetRequiredService<IFieldTypeRegistry>());
            foreach (var type in BlogSample.ContentTypes)
            {
                schema.AddContentType(type);
            }

            return schema.Build();
        });

        _app = builder.Build();
        _app.UseFluentCms();
        _app.MapFluentCmsOpenApi();
        _app.MapFluentCms();
    }

    public Task StartAsync() => _app.StartAsync(Xunit.TestContext.Current.CancellationToken);

    public HttpMessageHandler Handler() => _app.GetTestServer().CreateHandler();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
