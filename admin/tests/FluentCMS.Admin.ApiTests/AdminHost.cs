using System.Collections.Concurrent;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Identity;
using FluentCMS.Plugins.Media;
using FluentCMS.Samples.Blog;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The test host with the Blog sample and every first-party plugin, which the project references and the host discovers,
// as the reference host runs them (spec 7).
internal sealed class AdminHost : IAsyncDisposable
{
    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-admin-api-");

    // A test adds the types it needs beside the sample's.
    public AdminHost(int managementLimit = 10_000, params Type[] contentTypes)
    {
        Host = new FluentCmsTestHost()
            .WithContentTypes([.. BlogSample.ContentTypes, .. contentTypes])
            .WithServices(services =>
            {
                services.PostConfigure<MediaOptions>(options => options.Local.Root = _media.FullName);
                services.Configure<RateLimitOptions>(limits =>
                {
                    limits.Auth.PermitLimit = 10_000;
                    limits.Management.PermitLimit = managementLimit;
                });
            })
            .Configure(cms =>
            {
                cms.AddBlogSample();
                // The admin's client as a distribution registers it (spec 6.1), for the admin at https://localhost/admin/.
                cms.Clients.Applications[AdminTab.AdminClientId] = new ClientRegistration
                {
                    DisplayName = "FluentCMS admin",
                    RedirectUris = { AdminTab.AdminBase + "authentication/login-callback" },
                    PostLogoutRedirectUris = { AdminTab.AdminBase },
                    Scopes = { "openid", "profile", "email", "offline_access", "management" },
                };
            });
    }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public FluentCmsTestHost Host { get; }

    // The admin's client for a user of the role, signed in through the real authorize and token endpoints.
    public async Task<AdminClient> SignInAsync(string role, TimeProvider? time = null, Func<HttpMessageHandler, HttpMessageHandler>? wrap = null) =>
        await SignInAsync(await Host.CreateUserAsync(role, cancellationToken: Token), time, wrap);

    // A new sign-in of a user, as after a change that revoked its sessions.
    public async Task<AdminClient> SignInAsync(TestUser user, TimeProvider? time = null, Func<HttpMessageHandler, HttpMessageHandler>? wrap = null)
    {
        var tokens = await Host.SignInAsync(user);
        return new AdminClient(tokens.AccessToken, ServerHandler, time, wrap);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        try
        {
            _media.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    public HttpMessageHandler ServerHandler() => ((TestServer)Host.Services.GetRequiredService<IServer>()).CreateHandler();
}

// The services of AddManagementApi with a session that holds the user's token and the shell's site, over the test server.
internal sealed class AdminClient : IAccessTokenSource, ICurrentSite, IContentLocale, IRateLimitNotice, IDisposable
{
    private readonly string _accessToken;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public AdminClient(string accessToken, Func<HttpMessageHandler> server, TimeProvider? time, Func<HttpMessageHandler, HttpMessageHandler>? wrap)
    {
        _accessToken = accessToken;
        var services = new ServiceCollection();
        services.AddSingleton<IAccessTokenSource>(this);
        services.AddSingleton<ICurrentSite>(this);
        services.AddSingleton<IContentLocale>(this);
        services.AddSingleton<IRateLimitNotice>(this);
        if (time is not null)
        {
            services.AddSingleton(time);
        }

        services.AddManagementApi();
        services.Configure<ManagementApiOptions>(options =>
        {
            options.ApiBaseUrl = new Uri("https://localhost/");
            options.PrimaryHandler = () =>
            {
                var counting = new CountingHandler(Counts) { InnerHandler = server() };
                return wrap is null ? counting : wrap(counting);
            };
        });
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateScope();
    }

    public string? Site { get; set; }

    public string? Locale { get; set; }

    public ConcurrentDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

    public List<TimeSpan?> Notices { get; } = [];

    public IManagementApi Api => _scope.ServiceProvider.GetRequiredService<IManagementApi>();

    public IManagementEvents Events => _scope.ServiceProvider.GetRequiredService<IManagementEvents>();

    public ManagementClient Client => _scope.ServiceProvider.GetRequiredService<ManagementClient>();

    public SiteMetadata Metadata => _scope.ServiceProvider.GetRequiredService<SiteMetadata>();

    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(_accessToken);

    public ValueTask<bool> RefreshAsync(string rejectedToken, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public void TooManyRequests(TimeSpan? retryAfter) => Notices.Add(retryAfter);

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}

// Counts the requests that reach the server, by method, path and site.
internal sealed class CountingHandler(ConcurrentDictionary<string, int> counts) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var site = request.Headers.TryGetValues("X-Cms-Site", out var values) ? string.Join(",", values) : "";
        counts.AddOrUpdate($"{request.Method} {request.RequestUri!.AbsolutePath} {site}".TrimEnd(), 1, (_, count) => count + 1);
        return base.SendAsync(request, cancellationToken);
    }
}
