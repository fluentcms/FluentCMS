using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Shell;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// What the shell reads after sign-in and the sidebar it draws from it, on the Blog sample (spec 4.1), with the entries of
// core's administration pages as the shell registers them (spec 4.15).
public sealed class ShellTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task AsTheEditorTheSidebarListsTheTypesItMayReadAndNoAdministrationPage()
    {
        await using var admin = new AdminHost();
        using var shell = await ShellOfAsync(admin, BuiltInRoles.EditorName);

        await shell.Context.StartAsync();

        shell.Context.Status.ShouldBe(ShellStatus.Ready);
        shell.Context.SchemaRefused.ShouldBeFalse();
        var sections = ShellNavigation.Build(shell.Context, shell.Menu);
        sections.Select(section => section.Kind).ShouldBe([ShellSectionKind.Collections]);
        var links = sections.Single().Links;
        links.Select(link => link.Href).ShouldBe(["content/article", "content/author", "content/tag"], ignoreOrder: true);
        links.Single(link => link.Global).Href.ShouldBe("content/tag");
        shell.Menu.Entries.Count.ShouldBe(AdministrationAreas.All.Count);
        shell.Menu.Entries.ShouldAllBe(entry => !shell.Context.Allows(entry.Area!));
    }

    [Fact]
    public async Task AsTheSuperAdminTheSidebarAlsoListsTheAdministrationPages()
    {
        await using var admin = new AdminHost();
        using var shell = await ShellOfAsync(admin, BuiltInRoles.SuperAdminName);

        await shell.Context.StartAsync();

        var sections = ShellNavigation.Build(shell.Context, shell.Menu);
        sections.Single(section => section.Kind == ShellSectionKind.Administration)
            .Links.Select(link => link.Href).ShouldBe(["users", "roles", "api-keys", "members", "plugins", "jobs", "system/diagnostics"]);
        sections.Single(section => section.Kind == ShellSectionKind.Settings).Links.Select(link => link.Href).ShouldBe(["settings", "sites"]);
    }

    // The permissions of the areas are the server's own names, and the instance-only areas are those whose list answers 403
    // to a caller who holds the permission through a role of the site (#3178).
    [Fact]
    public async Task ACallerWhoHoldsThePermissionsThroughASiteRoleOnlyGetsTheSiteAreasAndNoInstanceOnlyOne()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        string[] held =
        [
            CorePermissions.SchemaRead, CorePermissions.UsersManage, CorePermissions.RolesManage, CorePermissions.ApiKeysManage, CorePermissions.MembersManage,
            CorePermissions.PluginsManage, CorePermissions.JobsManage, CorePermissions.SystemRead, CorePermissions.SitesManage, CorePermissions.Settings("media"),
        ];
        var role = new JsonObject
        {
            ["name"] = "Site administrator",
            ["siteId"] = DefaultSite.Id,
            ["grants"] = new JsonArray([.. held.Select(permission => (JsonNode)new JsonObject { ["permission"] = permission })]),
        };
        await super.Api.SendAsync(new ApiRequest(HttpMethod.Post, "roles") { Body = role }, Token);
        using var shell = await ShellOfAsync(admin, "Site administrator");

        await shell.Context.StartAsync();

        shell.Context.Status.ShouldBe(ShellStatus.Ready);
        AdministrationAreas.All.Select(area => area.Permission.Replace("*", "media", StringComparison.Ordinal)).ShouldBe(held[1..], ignoreOrder: true);
        ShellNavigation.Build(shell.Context, shell.Menu).Where(section => section.Kind is ShellSectionKind.Administration or ShellSectionKind.Settings)
            .SelectMany(section => section.Links).Select(link => link.Href).ShouldBe(["roles", "api-keys", "plugins", "settings", "sites"]);
        shell.Context.Allows(CorePermissions.UsersManage).ShouldBeTrue();
        shell.Context.AllowsForInstance(CorePermissions.UsersManage).ShouldBeFalse();

        // What the shell hides is what the server refuses: each list of an instance-only area answers 403 to this caller.
        using var client = await admin.SignInAsync(await admin.Host.CreateUserAsync("Site administrator", cancellationToken: Token));
        foreach (var path in (string[])["users", "members", "jobs", "system/diagnostics"])
        {
            var refused = await Should.ThrowAsync<ApiProblemException>(() => client.Api.SendAsync(new ApiRequest(HttpMethod.Get, path), Token));
            refused.Problem.Status.ShouldBe(403, path);
        }

        (await client.Api.GetRolesAsync(Token)).Select(found => found.Name).ShouldContain("Site administrator");
    }

    [Fact]
    public async Task ARoleThatReadsOneTypeGetsThatTypeOnly()
    {
        await using var admin = new AdminHost();
        await admin.Host.CreateRoleAsync("Article reader", [CorePermissions.SchemaRead, "content.article.read"], Token);
        using var shell = await ShellOfAsync(admin, "Article reader");

        await shell.Context.StartAsync();

        ShellNavigation.Build(shell.Context, shell.Menu).SelectMany(section => section.Links).Select(link => link.Href).ShouldBe(["content/article"]);
    }

    [Fact]
    public async Task AUserWithoutSchemaReadGetsNoTypesAndTheNotice()
    {
        await using var admin = new AdminHost();
        await admin.Host.CreateRoleAsync("Reader without schema", ["content.article.read"], Token);
        using var shell = await ShellOfAsync(admin, "Reader without schema");

        await shell.Context.StartAsync();

        shell.Context.Status.ShouldBe(ShellStatus.Ready);
        shell.Context.SchemaRefused.ShouldBeTrue();
        shell.Context.Schema.ShouldBeNull();
        ShellNavigation.Build(shell.Context, shell.Menu).ShouldBeEmpty();
    }

    [Fact]
    public async Task AUserWithNoRoleInAnySiteGetsTheNoSitePage()
    {
        await using var admin = new AdminHost();
        await admin.Host.CreateRoleAsync("Nothing", [], Token);
        using var shell = await ShellOfAsync(admin, "Nothing");

        await shell.Context.StartAsync();

        shell.Context.Status.ShouldBe(ShellStatus.NoSite);
        shell.Context.Site.ShouldBeNull();
    }

    private static async Task<ShellOfUser> ShellOfAsync(AdminHost admin, string role)
    {
        var user = await admin.Host.CreateUserAsync(role, cancellationToken: Token);
        var tokens = await admin.Host.SignInAsync(user);
        return new ShellOfUser(tokens.AccessToken, admin.ServerHandler);
    }

    // The shell's services over the test server, for a user signed in with the token.
    private sealed class ShellOfUser : IAccessTokenSource, IRateLimitNotice, IInterfaceLanguage, IDisposable
    {
        private readonly string _accessToken;
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;

        public ShellOfUser(string accessToken, Func<HttpMessageHandler> server)
        {
            _accessToken = accessToken;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IAccessTokenSource>(this);
            services.AddSingleton<IRateLimitNotice>(this);
            services.AddSingleton<ISessionStorage>(new TabStorage());
            // The app registers it at start, before the shell, which applies the user's preferences to it.
            services.AddSingleton(new FormatService(CultureInfo.InvariantCulture, TimeZoneInfo.Utc));
            services.AddSingleton<IInterfaceLanguage>(this);
            services.AddAdminShell();
            services.AddManagementApi();
            services.Configure<ManagementApiOptions>(options =>
            {
                options.ApiBaseUrl = new Uri("https://localhost/");
                options.PrimaryHandler = server;
            });
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            _scope = _provider.CreateScope();
        }

        public AdminContext Context => _scope.ServiceProvider.GetRequiredService<AdminContext>();

        // The menu as AddAdminShell registers it: the entries of core's administration pages.
        public AdminMenu Menu => _scope.ServiceProvider.GetRequiredService<AdminMenu>();

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(_accessToken);

        public ValueTask<bool> RefreshAsync(string rejectedToken, CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public void TooManyRequests(TimeSpan? retryAfter)
        {
        }

        // The app is not reloaded here: the shell goes on in the culture it has.
        public Task<bool> FollowAsync(string? preferred, bool atStart) => Task.FromResult(false);

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}
