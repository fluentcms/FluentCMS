using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Hosting;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Data.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The config.json that FluentCMS.Admin.Hosting builds from FluentCms:Admin, read by the admin's own reader: whatever an
// operator can configure, and the host starts with, the admin starts with too (admin spec 2.5, 6.1).
public sealed class HostedConfigurationTests : IAsyncDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-admin-hosting-");

    private WebApplication? _app;

    public static TheoryData<string, Dictionary<string, string?>> Settings() => new()
    {
        { "Production", [] },
        { "Development", new() { ["FluentCms:PublicOrigin"] = null } },
        {
            "Production",
            new()
            {
                ["FluentCms:PublicOrigin"] = "https://example.com/cms",
                ["FluentCms:ReverseProxy:PathBase"] = "/cms",
                ["FluentCms:Clients:AdminClientId"] = "acme-admin",
                ["FluentCms:Admin:ApiBaseUrl"] = "https://api.example.com/cms/",
                ["FluentCms:Admin:Branding:Name"] = "Acme — Ünïcode CMS",
                ["FluentCms:Admin:Branding:LogoUrl"] = "https://cdn.example.com/logo.svg?v=2",
                ["FluentCms:Admin:Branding:FaviconUrl"] = "images/favicon.png",
                ["FluentCms:Admin:Branding:AccentColor"] = "#0F6CBDCC",
                ["FluentCms:Admin:Cultures:Offered:0"] = "en",
                ["FluentCms:Admin:Cultures:Offered:1"] = "fr-CA",
                ["FluentCms:Admin:Cultures:Default"] = "FR-ca",
                ["FluentCms:Admin:PreviewOrigins:0"] = "https://www.example.com",
                ["FluentCms:Admin:PreviewOrigins:1"] = "http://localhost:3000/",
            }
        },
    };

    [Theory]
    [MemberData(nameof(Settings))]
    public async Task TheAdminReadsTheConfigJsonOfTheHostWithoutAProblem(string environment, Dictionary<string, string?> settings)
    {
        var client = await StartAsync(environment, settings);

        var json = await client.GetStringAsync(new Uri($"{PathBase(settings)}/admin/config.json", UriKind.Relative), AdminHost.Token);
        var startup = AdminConfigurationReader.Read(json);

        startup.Problems.ShouldBeEmpty(json);
        startup.UnknownMembers.ShouldBeEmpty(json);
        var configuration = startup.Configuration!;
        configuration.ClientId.ShouldBe(settings.GetValueOrDefault("FluentCms:Clients:AdminClientId") ?? "fluentcms-admin");
        configuration.Scopes.ShouldBe(["openid", "profile", "email", "offline_access", "management"]);
        if (settings.ContainsKey("FluentCms:Admin:Branding:Name"))
        {
            configuration.ApiBaseUrl.ShouldBe(new Uri("https://api.example.com/cms/"));
            configuration.Authority.OriginalString.ShouldBe("https://example.com/cms");
            configuration.Branding.Name.ShouldBe("Acme — Ünïcode CMS");
            configuration.Branding.LogoUrl.ShouldBe(new Uri("https://cdn.example.com/logo.svg?v=2"));
            configuration.Branding.FaviconUrl!.OriginalString.ShouldBe("images/favicon.png");
            configuration.Branding.AccentColor.ShouldBe("#0F6CBDCC");
            configuration.Cultures.Offered.ShouldBe(["en", "fr-CA"]);
            configuration.Cultures.Default.ShouldBe("fr-CA");
            configuration.PreviewOrigins.ShouldBe(["https://www.example.com", "http://localhost:3000"]);
        }
        else
        {
            configuration.ApiBaseUrl.ShouldBe(new Uri("https://localhost/"));
            configuration.Authority.OriginalString.ShouldBe("https://localhost/");
            configuration.PreviewOrigins.ShouldBeEmpty();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
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

    private static string PathBase(Dictionary<string, string?> settings) => settings.GetValueOrDefault("FluentCms:ReverseProxy:PathBase") ?? "";

    private async Task<HttpClient> StartAsync(string environment, Dictionary<string, string?> settings)
    {
        // The app's files play no part in config.json, so the host maps none but the index.html it checks at start.
        var manifest = Path.Combine(_directory.FullName, "empty.staticwebassets.endpoints.json");
        await File.WriteAllTextAsync(manifest, """{ "Version": 1, "ManifestType": "Build", "Endpoints": [] }""", AdminHost.Token);
        Directory.CreateDirectory(Path.Combine(_directory.FullName, "wwwroot"));
        await File.WriteAllTextAsync(Path.Combine(_directory.FullName, "wwwroot", "index.html"), """<html><head><base href="/" /></head></html>""", AdminHost.Token);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment, ContentRootPath = _directory.FullName });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FluentCms:Security:AllowGeneratedSigningKey"] = "true",
            ["FluentCms:Security:AllowUnencryptedKeyRing"] = "true",
            ["FluentCms:PublicOrigin"] = "https://localhost",
            ["FluentCms:Admin:Enabled"] = "true",
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddFluentCms(cms => cms.UseSqlite($"Data Source={Path.Combine(_directory.FullName, "cms.db")}").AddAdmin());

        _app = builder.Build();
        _app.UseFluentCms();
        _app.MapFluentCms();
        _app.MapFluentCmsAdmin(manifest);
        await _app.StartAsync(AdminHost.Token);

        var client = _app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost/");
        return client;
    }
}
