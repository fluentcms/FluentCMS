using System.Collections.Concurrent;
using System.Net;
using FluentCMS.Admin.Hosting;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Data.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    private const string OtherClientId = "website";

    private const string ThirdClientId = "kiosk";

    // Core's log events of the clients the options no longer list: deleted, and kept for a list of none.
    private const int ClientDeleted = 9002;
    private const int ClientsKept = 9003;

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

    // An admin of the operator's own, served under the id and the display name of the package's client, which the options list.
    [Fact]
    public async Task AListedAdminClientWithTheRegistrationsDisplayNameIsKeptWhileTheAdminIsOff()
    {
        var own = Settings(enabled: false);
        own[$"FluentCms:Clients:Applications:{AdminClientId}:DisplayName"] = "FluentCMS admin";
        own[$"FluentCms:Clients:Applications:{AdminClientId}:RedirectUris:0"] = "https://admin.example.com/authentication/login-callback";
        own[$"FluentCms:Clients:Applications:{AdminClientId}:Scopes:0"] = "openid";
        await StartAsync(own);
        var client = (await AdminClientAsync()).ShouldNotBeNull();

        await StartAsync(own);

        (await AdminClientAsync()).ShouldBe(client);
    }

    // A client registered some other way than from the options is not the package's, whatever its name.
    [Fact]
    public async Task AnAdminClientNotFromTheOptionsIsKeptWhenTheAdminIsOff()
    {
        await StartAsync(Settings(enabled: false));
        await using (var scope = _app!.Services.CreateAsyncScope())
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = AdminClientId,
                DisplayName = "FluentCMS admin",
                ClientType = OpenIddictConstants.ClientTypes.Public,
            };
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
            await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(descriptor, AdminHost.Token);
        }

        var client = (await AdminClientAsync()).ShouldNotBeNull();

        await StartAsync(Settings(enabled: false));

        (await AdminClientAsync()).ShouldBe(client);
    }

    // A delete the database refuses, as a token arriving meanwhile makes it, leaves the row and lets the host start.
    [Fact]
    public async Task ADeleteTheDatabaseRefusesLeavesTheClientAndTheHostStarts()
    {
        await StartAsync(Settings(enabled: true));
        var client = (await AdminClientAsync()).ShouldNotBeNull();
        await StopAsync();
        await using (var connection = new SqliteConnection(ConnectionString))
        {
            await connection.OpenAsync(AdminHost.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER refuse BEFORE DELETE ON cms_oidc_application BEGIN SELECT RAISE(ABORT, 'refused'); END;";
            await command.ExecuteNonQueryAsync(AdminHost.Token);
        }

        var logs = new LogCapture();
        await StartAsync(Settings(enabled: false), logs);

        (await AdminClientAsync()).ShouldBe(client);
        logs.Entries.ShouldContain(entry => entry.Id == 40 && entry.Level == LogLevel.Warning);
    }

    // With another client listed the host deletes the admin's itself, and keeps the one listed.
    [Fact]
    public async Task TurningTheAdminOffBesideAnotherClientDeletesOnlyTheAdminsClient()
    {
        var on = WithAnotherClient(Settings(enabled: true));
        await StartAsync(on);
        (await AdminClientAsync()).ShouldNotBeNull();
        var other = (await ClientAsync(OtherClientId)).ShouldNotBeNull();

        await StartAsync(WithAnotherClient(Settings(enabled: false)));

        (await AdminClientAsync()).ShouldBeNull();
        (await ClientAsync(OtherClientId)).ShouldBe(other);
    }

    // A start that lost FluentCms:Clients, with the admin on by default: the client the admin adds does not make the list one
    // of the operator's, so the host keeps the clients it registered, with their sessions and tokens, as it did without it.
    [Fact]
    public async Task LosingTheClientsSectionKeepsTheConfiguredClientsWithTheAdminOnByDefault()
    {
        await StartAsync(WithAnotherClient(Unset()));
        var other = (await ClientAsync(OtherClientId)).ShouldNotBeNull();
        var (authorization, token) = await GrantAsync(OtherClientId);

        var logs = new LogCapture();
        await StartAsync(Unset(), logs);

        (await ClientAsync(OtherClientId)).ShouldBe(other);
        (await AdminClientAsync()).ShouldNotBeNull();
        (await ExistsAsync(authorization, token)).ShouldBe((true, true));
        logs.Entries.ShouldContain((ClientsKept, LogLevel.Warning));
        logs.Entries.ShouldNotContain(entry => entry.Id == ClientDeleted);
    }

    // The control: while the operator still lists a client, one it no longer lists goes, with the admin on as with it off.
    [Fact]
    public async Task WithAClientStillListedOneNoLongerListedIsDeletedWithTheAdminOn()
    {
        var both = WithAnotherClient(Unset());
        both[$"FluentCms:Clients:Applications:{ThirdClientId}:RedirectUris:0"] = "https://kiosk.example.com/callback";
        both[$"FluentCms:Clients:Applications:{ThirdClientId}:Scopes:0"] = "openid";
        await StartAsync(both);
        var other = (await ClientAsync(OtherClientId)).ShouldNotBeNull();
        (await ClientAsync(ThirdClientId)).ShouldNotBeNull();
        var (authorization, token) = await GrantAsync(ThirdClientId);

        await StartAsync(WithAnotherClient(Unset()));

        (await ClientAsync(OtherClientId)).ShouldBe(other);
        (await ClientAsync(ThirdClientId)).ShouldBeNull();
        (await ExistsAsync(authorization, token)).ShouldBe((false, false));
    }

    // A deployment that lived with a list of none before the upgrade, the admin off as it was by default, keeps its clients
    // at the first start with the admin on.
    [Fact]
    public async Task AListOfNoneBeforeTheUpgradeKeepsItsClientsWithTheAdminOn()
    {
        await StartAsync(WithAnotherClient(Settings(enabled: false)));
        var other = (await ClientAsync(OtherClientId)).ShouldNotBeNull();
        await StartAsync(Settings(enabled: false));
        (await ClientAsync(OtherClientId)).ShouldBe(other);

        await StartAsync(Unset());

        (await ClientAsync(OtherClientId)).ShouldBe(other);
        (await AdminClientAsync()).ShouldNotBeNull();
    }

    // A client the host lists in code with Configure<ClientOptions>, after the binding of the section, counts as listed.
    [Fact]
    public async Task AClientTheHostListsWithConfigureCountsAsListed()
    {
        await StartAsync(WithAnotherClient(Unset()));
        (await ClientAsync(OtherClientId)).ShouldNotBeNull();

        await StartAsync(Unset(), services: services => services.Configure<ClientOptions>(options => options.Applications[ThirdClientId] = Public(ThirdClientId)));

        (await ClientAsync(ThirdClientId)).ShouldNotBeNull();
        (await ClientAsync(OtherClientId)).ShouldBeNull();
    }

    // Clients the host lists in a post-configure of its own are the operator's too, whatever order it runs in: one it drops
    // goes, with its session, as one dropped from the configuration does.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AClientDroppedFromTheHostsPostConfigureIsDeleted(bool adminOn)
    {
        var settings = adminOn ? Unset() : Settings(enabled: false);
        await StartAsync(settings, services: PostConfigured(OtherClientId, ThirdClientId));
        var other = (await ClientAsync(OtherClientId)).ShouldNotBeNull();
        var (authorization, token) = await GrantAsync(ThirdClientId);

        var logs = new LogCapture();
        await StartAsync(settings, logs, PostConfigured(OtherClientId));

        (await ClientAsync(OtherClientId)).ShouldBe(other);
        (await ClientAsync(ThirdClientId)).ShouldBeNull();
        (await ExistsAsync(authorization, token)).ShouldBe((false, false));
        logs.Entries.ShouldContain((ClientDeleted, LogLevel.Warning));
        logs.Entries.ShouldNotContain(entry => entry.Id == ClientsKept);
    }

    // A container built over the same services before the host's own, as a host's code may build one, runs the admin's
    // post-configure on the same options first; a lost section still keeps the clients.
    [Fact]
    public async Task ASecondContainerBeforeTheHostsKeepsTheClientsOfALostSection()
    {
        await StartAsync(WithAnotherClient(Unset()));
        var other = (await ClientAsync(OtherClientId)).ShouldNotBeNull();
        var (authorization, token) = await GrantAsync(OtherClientId);

        var logs = new LogCapture();
        await StartAsync(Unset(), logs, services =>
        {
            using var early = services.BuildServiceProvider();
            early.GetRequiredService<ClientOptions>().Applications.ShouldContainKey(AdminClientId);
        });

        (await ClientAsync(OtherClientId)).ShouldBe(other);
        (await ExistsAsync(authorization, token)).ShouldBe((true, true));
        logs.Entries.ShouldContain((ClientsKept, LogLevel.Warning));
        logs.Entries.ShouldNotContain(entry => entry.Id == ClientDeleted);
    }

    // The admin's client under a new id is the package's only client, so the options list none of the operator's and the row
    // under the old id is kept, a public client whose addresses are the host's own, as for a lost section.
    [Fact]
    public async Task AChangedAdminClientIdKeepsTheRowOfTheOldOne()
    {
        await StartAsync(Unset());
        var old = (await AdminClientAsync()).ShouldNotBeNull();

        var renamed = Unset();
        renamed["FluentCms:Clients:AdminClientId"] = "team-admin";
        var logs = new LogCapture();
        await StartAsync(renamed, logs);

        (await AdminClientAsync()).ShouldBe(old);
        (await ClientAsync("team-admin")).ShouldNotBeNull();
        logs.Entries.ShouldContain((ClientsKept, LogLevel.Warning));
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

    // Nothing set about the admin, so that it is on by its default.
    private static Dictionary<string, string?> Unset()
    {
        var settings = Settings(enabled: true);
        settings.Remove("FluentCms:Admin:Enabled");
        return settings;
    }

    private static ClientRegistration Public(string clientId)
    {
        var client = new ClientRegistration();
        client.RedirectUris.Add($"https://{clientId}.example.com/callback");
        client.Scopes.Add("openid");
        return client;
    }

    // The host's own post-configure of the clients, which lists these.
    private static Action<IServiceCollection> PostConfigured(params string[] clientIds) => services => services.PostConfigure<ClientOptions>(options =>
    {
        foreach (var clientId in clientIds)
        {
            options.Applications[clientId] = Public(clientId);
        }
    });

    // A session of the client: a permanent authorization and a refresh token under it.
    private async Task<(string Authorization, string Token)> GrantAsync(string clientId)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var application = (await applications.FindByClientIdAsync(clientId, AdminHost.Token)).ShouldNotBeNull();
        var applicationId = await applications.GetIdAsync(application, AdminHost.Token);
        var now = DateTimeOffset.UtcNow;
        var authorization = await authorizations.CreateAsync(new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = applicationId,
            Subject = "editor",
            Status = OpenIddictConstants.Statuses.Valid,
            Type = OpenIddictConstants.AuthorizationTypes.Permanent,
            CreationDate = now,
        }, AdminHost.Token);
        var authorizationId = (await authorizations.GetIdAsync(authorization, AdminHost.Token)).ShouldNotBeNull();
        var token = await tokens.CreateAsync(new OpenIddictTokenDescriptor
        {
            ApplicationId = applicationId,
            AuthorizationId = authorizationId,
            Subject = "editor",
            Status = OpenIddictConstants.Statuses.Valid,
            Type = OpenIddictConstants.TokenTypeHints.RefreshToken,
            CreationDate = now,
            ExpirationDate = now.AddDays(1),
        }, AdminHost.Token);
        return (authorizationId, (await tokens.GetIdAsync(token, AdminHost.Token)).ShouldNotBeNull());
    }

    private async Task<(bool Authorization, bool Token)> ExistsAsync(string authorization, string token)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().FindByIdAsync(authorization, AdminHost.Token) is not null,
            await scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>().FindByIdAsync(token, AdminHost.Token) is not null);
    }

    private static List<FluentCmsConfigurationException> Refusals(Exception exception) => exception switch
    {
        AggregateException aggregate => [.. aggregate.InnerExceptions.SelectMany(Refusals)],
        FluentCmsConfigurationException refusal => [refusal],
        _ => throw new ShouldAssertException($"Not a configuration refusal: {exception}"),
    };

    private static Dictionary<string, string?> WithAnotherClient(Dictionary<string, string?> settings)
    {
        settings[$"FluentCms:Clients:Applications:{OtherClientId}:RedirectUris:0"] = "https://app.example.com/callback";
        settings[$"FluentCms:Clients:Applications:{OtherClientId}:Scopes:0"] = "openid";
        return settings;
    }

    private Task<string?> AdminClientAsync() => ClientAsync(AdminClientId);

    // The id of the client's row, or null when there is none.
    private async Task<string?> ClientAsync(string clientId)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        return await applications.FindByClientIdAsync(clientId, AdminHost.Token) is { } application
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

    private string ConnectionString => $"Data Source={Path.Combine(_directory.FullName, "cms.db")}";

    // Stops the host a test started before, if any, and starts it again on the same database.
    private async Task<HttpClient> StartAsync(Dictionary<string, string?> settings, LogCapture? logs = null, Action<IServiceCollection>? services = null)
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
        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        builder.Services.AddFluentCms(cms => cms.UseSqlite(ConnectionString).AddAdmin());
        services?.Invoke(builder.Services);

        _app = builder.Build();
        _app.UseFluentCms();
        _app.MapFluentCms();
        _app.MapFluentCmsAdmin(manifest);
        await _app.StartAsync(AdminHost.Token);

        var client = _app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost/");
        return client;
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public ConcurrentQueue<(int Id, LogLevel Level)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCapture capture) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                capture.Entries.Enqueue((eventId.Id, logLevel));
        }
    }
}
