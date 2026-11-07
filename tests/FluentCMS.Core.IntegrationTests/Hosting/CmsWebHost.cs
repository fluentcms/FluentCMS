using FluentCMS.Core.DependencyInjection;
using FluentCMS.Data.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.IntegrationTests.Hosting;

// A real web host on a SQLite file of its own, set up the way a host's Program.cs sets it up.
internal sealed class CmsWebHost : IAsyncDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-hosting-tests-");

    private WebApplication? _app;

    public string DatabaseFile => Path.Combine(_directory.FullName, "cms.db");

    public WebApplication Build(
        string environment,
        Dictionary<string, string?>? settings = null,
        Action<WebApplicationBuilder>? configure = null,
        Action<WebApplication>? pipeline = null,
        Action<FluentCmsBuilder>? cms = null,
        Action<IEndpointConventionBuilder>? endpoints = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();

        // These hosts send no links; a test of the public origin or key ring refusal sets the flag back to false.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FluentCms:Security:AllowRequestOrigin"] = "true",
            ["FluentCms:Security:AllowUnencryptedKeyRing"] = "true",
        });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.Services.AddFluentCms(options =>
        {
            options.UseSqlite($"Data Source={DatabaseFile};Pooling=False");
            cms?.Invoke(options);
        });
        // These hosts sign no tokens; a test about the signing key registers the status it needs after this one.
        builder.Services.AddSingleton<ISigningKeyStatus>(new ConfiguredSigningKey());
        configure?.Invoke(builder);

        var app = builder.Build();
        if (pipeline is null)
        {
            app.UseFluentCms();
        }
        else
        {
            pipeline(app);
        }

        var conventions = app.MapFluentCms();
        endpoints?.Invoke(conventions);
        return _app = app;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            // A host that failed to start cannot be stopped.
            if (_app.Lifetime.ApplicationStarted.IsCancellationRequested)
            {
                await _app.StopAsync();
            }

            await _app.DisposeAsync();
        }

        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class ConfiguredSigningKey : ISigningKeyStatus
    {
        public bool IsGenerated => false;
    }
}
