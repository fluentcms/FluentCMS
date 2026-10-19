using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using FluentCMS.Core.Identity;
using FluentCMS.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests;

// What an install keeps between restarts: its database and its Data Protection key ring.
internal sealed class TemporaryInstall : IDisposable
{
    private readonly TemporarySqliteDatabase _database = new();

    public string KeysDirectory { get; } = Path.Combine(Path.GetTempPath(), $"fluentcms-keys-{Guid.NewGuid():N}");

    public Dictionary<string, string?> Configuration => new(_database.Configuration)
    {
        ["DataProtection:KeysDirectory"] = KeysDirectory,
    };

    public void Dispose()
    {
        _database.Dispose();

        if (Directory.Exists(KeysDirectory))
        {
            Directory.Delete(KeysDirectory, recursive: true);
        }
    }
}

// The web host on a free port, with a client that keeps cookies like a browser and does not follow redirects.
internal sealed partial class AdminHost : IAsyncDisposable
{
    public const string AuthCookie = ".AspNetCore.Identity.Application";

    public static readonly NewUser Owner = new("owner@example.com", "Owner", "Str0ng!Passw0rd");

    private readonly bool _ownsApp;

    private AdminHost(WebApplication app, CookieContainer cookies, bool ownsApp = true)
    {
        App = app;
        Cookies = cookies;
        _ownsApp = ownsApp;
        Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = cookies })
        {
            BaseAddress = new Uri(app.Urls.Single()),
        };
    }

    public WebApplication App { get; }

    public CookieContainer Cookies { get; }

    public HttpClient Client { get; }

    public bool IsSignedIn => Cookies.GetCookies(Client.BaseAddress!).Any(cookie => cookie.Name == AuthCookie);

    public static async Task<AdminHost> StartAsync(
        TemporaryInstall install,
        string[]? args = null,
        IDictionary<string, string?>? settings = null,
        CookieContainer? cookies = null,
        Action<WebApplicationBuilder>? configure = null)
    {
        var app = FluentCmsHost.BuildWebApplication(args ?? [], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(install.Configuration);
            builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
            configure?.Invoke(builder);
        });

        await app.StartAsync();
        return new AdminHost(app, cookies ?? new CookieContainer());
    }

    public async Task<Guid> CreateFirstAdministratorAsync(NewUser? user = null)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<IFirstRunSetup>().CreateFirstAdministratorAsync(user ?? Owner);
        return created.Value;
    }

    // Another browser on the same host, with cookies of its own; disposing it leaves the host running.
    public AdminHost OpenAnotherBrowser()
    {
        return new AdminHost(App, new CookieContainer(), ownsApp: false);
    }

    // Calls the directory outside a request, as a request signed in as that user would, since every change names its initiator.
    public Task<T> AsUserAsync<T>(Guid userId, Func<IUserDirectory, Task<T>> action)
    {
        return AsUserAsync<IUserDirectory, T>(userId, action);
    }

    public Task<T> AsUserAsync<TService, T>(Guid userId, Func<TService, Task<T>> action)
        where TService : notnull
    {
        return AsAsync(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(FluentCmsClaimTypes.DisplayName, "Test administrator")],
                authenticationType: "test"),
            action);
    }

    // As a request an agent credential signed would, for what a test drives without a tool.
    public Task<T> AsAgentAsync<TService, T>(Guid credentialId, Func<TService, Task<T>> action)
        where TService : notnull
    {
        return AsAsync(
            new ClaimsIdentity(
                [new Claim(FluentCmsClaimTypes.AgentCredentialId, credentialId.ToString()), new Claim(FluentCmsClaimTypes.DisplayName, "Test agent")],
                authenticationType: "test"),
            action);
    }

    private async Task<T> AsAsync<TService, T>(ClaimsIdentity identity, Func<TService, Task<T>> action)
        where TService : notnull
    {
        var accessor = App.Services.GetRequiredService<IHttpContextAccessor>();
        await using var scope = App.Services.CreateAsyncScope();

        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(identity) };
        try
        {
            return await action(scope.ServiceProvider.GetRequiredService<TService>());
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    public async Task<IReadOnlyList<UserSummary>> UsersAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IUserDirectory>().ListAsync();
    }

    // Fetches the page with the form first, as a browser would, for the anti-forgery cookie and the token that goes with it.
    // A null handler is for a form that posts to a host endpoint rather than to a page's named form.
    public async Task<HttpResponseMessage> SubmitAsync(string path, string? handler, IReadOnlyDictionary<string, string> fields, string? from = null)
    {
        var page = await Client.GetStringAsync(from ?? path);
        var token = AntiforgeryToken().Match(page);
        Assert.True(token.Success, $"{from ?? path} has no anti-forgery token.");

        var form = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
        };
        if (handler is not null)
        {
            form["_handler"] = handler;
        }

        return await Client.PostAsync(path, new FormUrlEncodedContent(form));
    }

    public Task<HttpResponseMessage> SignInAsync(NewUser user, string path = "/sign-in")
    {
        return SignInAsync(user.Email, user.Password, path);
    }

    public Task<HttpResponseMessage> SignInAsync(string email, string password, string path = "/sign-in")
    {
        return SubmitAsync(path, "sign-in", new Dictionary<string, string>
        {
            ["Form.Email"] = email,
            ["Form.Password"] = password,
        });
    }

    public string? LocationOf(HttpResponseMessage response)
    {
        return response.Headers.Location is { } location
            ? new Uri(Client.BaseAddress!, location).PathAndQuery
            : null;
    }

    // Follows redirects the way a browser would and returns the path it ends on.
    public async Task<(string Path, HttpResponseMessage Response)> FollowAsync(string path)
    {
        for (var hops = 0; hops < 10; hops++)
        {
            var response = await Client.GetAsync(path);
            if (LocationOf(response) is not { } next)
            {
                return (path, response);
            }

            path = next;
        }

        throw new InvalidOperationException($"Too many redirects from {path}.");
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (_ownsApp)
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
