using System.Net;
using FluentCMS.Admin.Hosting;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Data.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// What FluentCMS.Admin.Hosting does with a host beside config.json (admin spec 6.1): the admin client's row as the admin is
// turned off or kept on, a path base it refuses at start, and the page's opener policy with preview on. One database across
// the starts of a test, as an instance restarted with other settings.
public sealed class HostingStartTests : IAsyncDisposable
{
    private const string AdminClientId = "fluentcms-admin";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-admin-hosting-start-");

    private WebApplication? _app;

    [Fact]
    public async Task TurningTheAdminOffDeletesTheClientItRegistered()
    {
        await StartAsync(Settings(enabled: true));
        (await AdminClientAsync()).ShouldNotBeNull();

        await StartAsync(Settings(enabled: false));

        (await AdminClientAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task KeepingTheAdminOnKeepsItsClient()
    {
        await StartAsync(Settings(enabled: true));
        var client = (await AdminClientAsync()).ShouldNotBeNull();

        await StartAsync(Settings(enabled: true));

        (await AdminClientAsync()).ShouldBe(client);
    }

    // With no client listed the host keeps those it registered, and one the operator configured is not the admin's own.
    [Fact]
    public async Task AnAdminClientTheOperatorConfiguredIsKeptWhenTheAdminIsTurnedOff()
    {
        var configured = Settings(enabled: true);
        configured[$"FluentCms:Clients:Applications:{AdminClientId}:RedirectUris:0"] = "https://admin.example.com/authentication/login-callback";
        configured[$"FluentCms:Clients:Applications:{AdminClientId}:Scopes:0"] = "openid";
        await StartAsync(configured);
        var client = (await AdminClientAsync()).ShouldNotBeNull();

        await StartAsync(Settings(enabled: false));

        (await AdminClientAsync()).ShouldBe(client);
    }

    [Theory]
    [InlineData("/..")]
    [InlineData("/.")]
    [InlineData("/a/../b")]
    public async Task APathBaseWithASegmentOfDotsOnlyStopsTheHost(string pathBase)
    {
        var settings = Settings(enabled: true);
        settings["FluentCms:ReverseProxy:PathBase"] = pathBase;

        var exception = await Should.ThrowAsync<Exception>(() => StartAsync(settings));

        var refusal = Refusals(exception).ShouldHaveSingleItem();
        refusal.Code.ShouldBe("FCMS1101");
        refusal.Message.ShouldContain($"FluentCms:ReverseProxy:PathBase is '{pathBase}'");
    }

    [Fact]
    public async Task APathBaseWithADotInASegmentServesTheAdminThere()
    {
        var settings = Settings(enabled: true);
        settings["FluentCms:ReverseProxy:PathBase"] = "/a.b";
        var client = await StartAsync(settings);

        using var page = await client.GetAsync(new Uri("/a.b/admin/", UriKind.Relative), AdminHost.Token);

        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync(AdminHost.Token)).ShouldContain("<base href=\"/a.b/admin/\" />");
    }

    // Preview loads in a frame, which the policy leaves alone, and nothing of the admin needs the opener of a window of
    // another origin, so the page keeps same-origin with preview on.
    [Fact]
    public async Task ThePageKeepsTheSameOriginOpenerPolicyWithPreviewOn()
    {
        var settings = Settings(enabled: true);
        settings["FluentCms:Admin:PreviewOrigins:0"] = "https://www.example.com";
        var client = await StartAsync(settings);

        foreach (var path in new[] { "/admin/", "/admin/content/article/1" })
        {
            using var page = await client.GetAsync(new Uri(path, UriKind.Relative), AdminHost.Token);
            page.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            page.Headers.GetValues("Cross-Origin-Opener-Policy").ShouldHaveSingleItem().ShouldBe("same-origin", path);
            page.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldContain("frame-src https://www.example.com;", Case.Sensitive, path);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // No client but the admin's, so that the host's rule for a list of none applies.
    private static Dictionary<string, string?> Settings(bool enabled) => new()
    {
        ["FluentCms:Security:AllowGeneratedSigningKey"] = "true",
        ["FluentCms:Security:AllowUnencryptedKeyRing"] = "true",
        ["FluentCms:PublicOrigin"] = "https://localhost",
        ["FluentCms:Admin:Enabled"] = enabled ? "true" : "false",
    };

    private static List<FluentCmsConfigurationException> Refusals(Exception exception) => exception switch
    {
        AggregateException aggregate => [.. aggregate.InnerExceptions.SelectMany(Refusals)],
        FluentCmsConfigurationException refusal => [refusal],
        _ => throw new ShouldAssertException($"Not a configuration refusal: {exception}"),
    };

    // The id of the admin client's row, or null when there is none.
    private async Task<string?> AdminClientAsync()
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        return await applications.FindByClientIdAsync(AdminClientId, AdminHost.Token) is { } application
            ? await applications.GetIdAsync(application, AdminHost.Token)
            : null;
    }

    private async Task StopAsync()
    {
        if (_app is null)
        {
            return;
        }

        if (_app.Lifetime.ApplicationStarted.IsCancellationRequested)
        {
            await _app.StopAsync();
        }

        await _app.DisposeAsync();
        _app = null;
    }

    // Stops the host a test started before, if any, and starts it again on the same database.
    private async Task<HttpClient> StartAsync(Dictionary<string, string?> settings)
    {
        await StopAsync();

        // The app's files play no part here, so the host maps none but the index.html it checks at start.
        var manifest = Path.Combine(_directory.FullName, "empty.staticwebassets.endpoints.json");
        await File.WriteAllTextAsync(manifest, """{ "Version": 1, "ManifestType": "Build", "Endpoints": [] }""", AdminHost.Token);
        Directory.CreateDirectory(Path.Combine(_directory.FullName, "wwwroot"));
        await File.WriteAllTextAsync(Path.Combine(_directory.FullName, "wwwroot", "index.html"), """<html><head><base href="/" /></head></html>""", AdminHost.Token);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", ContentRootPath = _directory.FullName });
        builder.WebHost.UseTestServer();
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
