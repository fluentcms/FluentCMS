using FluentCMS.Admin.Api;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Shell;
using FluentCMS.Core.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// What the shell reads after sign-in and the sidebar it draws from it, on the Blog sample (spec 4.1).
public sealed class ShellTests
{
    // Core's administration pages as the issues of A6 will add them, each behind its permission.
    private static readonly AdminMenu Administration = new()
    {
        Entries =
        [
            new("users", "Shell.Sidebar.Administration", AdminMenuSection.Administration, CorePermissions.UsersManage),
            new("roles", "Shell.Sidebar.Administration", AdminMenuSection.Administration, CorePermissions.RolesManage),
            new("api-keys", "Shell.Sidebar.Administration", AdminMenuSection.Administration, CorePermissions.ApiKeysManage),
            new("sites", "Shell.Sidebar.Administration", AdminMenuSection.Administration, CorePermissions.SitesManage),
            new("jobs", "Shell.Sidebar.Administration", AdminMenuSection.Administration, CorePermissions.JobsManage),
        ],
    };

    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task AsTheEditorTheSidebarListsTheTypesItMayReadAndNoAdministrationPage()
    {
        await using var admin = new AdminHost();
        using var shell = await ShellOfAsync(admin, BuiltInRoles.EditorName);

        await shell.Context.StartAsync();

        shell.Context.Status.ShouldBe(ShellStatus.Ready);
        shell.Context.SchemaRefused.ShouldBeFalse();
        var sections = ShellNavigation.Build(shell.Context, Administration);
        sections.Select(section => section.Kind).ShouldBe([ShellSectionKind.Collections]);
        var links = sections.Single().Links;
        links.Select(link => link.Href).ShouldBe(["content/article", "content/author", "content/tag"], ignoreOrder: true);
        links.Single(link => link.Global).Href.ShouldBe("content/tag");
        Administration.Entries.ShouldAllBe(entry => !shell.Context.Allows(entry.Permission!));
    }

    [Fact]
    public async Task AsTheSuperAdminTheSidebarAlsoListsTheAdministrationPages()
    {
        await using var admin = new AdminHost();
        using var shell = await ShellOfAsync(admin, BuiltInRoles.SuperAdminName);

        await shell.Context.StartAsync();

        ShellNavigation.Build(shell.Context, Administration).Single(section => section.Kind == ShellSectionKind.Administration)
            .Links.Select(link => link.Href).ShouldBe(["users", "roles", "api-keys", "sites", "jobs"]);
    }

    [Fact]
    public async Task ARoleThatReadsOneTypeGetsThatTypeOnly()
    {
        await using var admin = new AdminHost();
        await admin.Host.CreateRoleAsync("Article reader", [CorePermissions.SchemaRead, "content.article.read"], Token);
        using var shell = await ShellOfAsync(admin, "Article reader");

        await shell.Context.StartAsync();

        ShellNavigation.Build(shell.Context, Administration).SelectMany(section => section.Links).Select(link => link.Href).ShouldBe(["content/article"]);
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
        ShellNavigation.Build(shell.Context, Administration).ShouldBeEmpty();
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
    private sealed class ShellOfUser : IAccessTokenSource, IRateLimitNotice, IDisposable
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

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(_accessToken);

        public ValueTask<bool> RefreshAsync(string rejectedToken, CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public void TooManyRequests(TimeSpan? retryAfter)
        {
        }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}
