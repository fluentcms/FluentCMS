using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Interop;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace FluentCMS.Admin.ApiTests;

// A tab of the admin at https://localhost/admin/: its real sign-in, its sessionStorage and its Management API client, over
// the test server. The browser's part, following the redirects of the hosted pages with the user's cookie, is HostedBrowser.
internal sealed class AdminTab : IDisposable
{
    public const string AdminBase = "https://localhost/admin/";
    public const string AdminClientId = "fluentcms-admin";

    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public AdminTab(FluentCmsTestHost host, Func<HttpMessageHandler> server)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // As config.json writes it by default: the API's origin, so the issuer is compared without its trailing slash.
        services.AddSingleton(new AdminConfiguration { ApiBaseUrl = new Uri("https://localhost/"), Authority = new Uri("https://localhost", UriKind.Absolute) });
        services.AddSingleton<NavigationManager>(Navigation);
        services.AddSingleton<TimeProvider>(host.Time);
        services.AddAdminAuthentication();
        services.Replace(ServiceDescriptor.Scoped<ISessionStorage>(_ => Storage));
        services.Replace(ServiceDescriptor.Scoped<ITabChannel, NoOtherTab>());
        services.Configure<AuthorityOptions>(options => options.PrimaryHandler = server);
        services.AddManagementApi();
        services.Configure<ManagementApiOptions>(options =>
        {
            options.ApiBaseUrl = new Uri("https://localhost/");
            options.PrimaryHandler = server;
        });
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateScope();
    }

    public RecordingNavigation Navigation { get; } = new();

    public TabStorage Storage { get; } = new();

    public AdminSession Session => _scope.ServiceProvider.GetRequiredService<AdminSession>();

    public ManagementClient Client => _scope.ServiceProvider.GetRequiredService<ManagementClient>();

    public string? RefreshToken => Storage.Get(AdminSession.SessionKey) is { } json
        ? JsonDocument.Parse(json).RootElement.GetProperty("refresh_token").GetString()
        : null;

    // Starts the sign-in, follows the hosted pages with the browser, and completes it on the callback.
    public async Task<SignInCompletion> SignInAsync(HostedBrowser browser, string route = "content/article", Func<HttpResponseMessage, Task<HttpResponseMessage>>? onHostedPage = null)
    {
        await Session.SignInAsync(route, AdminHost.Token);
        var authorize = Navigation.Last.ShouldNotBeNull();
        authorize.GetLeftPart(UriPartial.Path).ShouldBe("https://localhost/connect/authorize");

        var callback = await browser.FollowToAsync(authorize.PathAndQuery, AdminBase, onHostedPage);
        callback.GetLeftPart(UriPartial.Path).ShouldBe(AdminBase + AdminSession.CallbackRoute);
        var query = HttpUtility.ParseQueryString(callback.Query);
        return await Session.CompleteSignInAsync(query["code"], query["state"], query["error"], AdminHost.Token);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}

internal sealed class TabStorage : ISessionStorage
{
    private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => _items.Keys;

    public string? Get(string key) => _items.GetValueOrDefault(key);

    public ValueTask<string?> GetAsync(string key) => ValueTask.FromResult(_items.GetValueOrDefault(key));

    public ValueTask SetAsync(string key, string value)
    {
        _items[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key)
    {
        _items.Remove(key);
        return ValueTask.CompletedTask;
    }
}

// The tab is the only admin tab of its browser, so no other tab holds its session.
internal sealed class NoOtherTab : ITabChannel
{
    public ValueTask<bool> IsHeldElsewhereAsync(string tabId) => ValueTask.FromResult(false);

    public ValueTask HoldAsync(string tabId) => ValueTask.CompletedTask;
}

// The whole-page navigations of the tab; a test plays the browser's part of each.
internal sealed class RecordingNavigation : NavigationManager
{
    public RecordingNavigation() => Initialize(AdminTab.AdminBase, AdminTab.AdminBase + "content/article");

    public List<Uri> Navigations { get; } = [];

    public Uri? Last => Navigations.LastOrDefault();

    protected override void NavigateToCore(string uri, NavigationOptions options) => Navigations.Add(ToAbsoluteUri(uri));
}

// A browser on the hosted pages of the test host: it keeps cookies, follows no redirect on its own, and posts a page's form
// with the antiforgery token the page rendered.
internal sealed partial class HostedBrowser(Func<HttpMessageHandler> server, string? signInCookie) : IDisposable
{
    private static readonly Uri Origin = new("https://localhost/");

    private readonly CookieContainer _cookies = Cookies(signInCookie);
    private HttpClient? _client;

    private HttpClient Client => _client ??= new HttpClient(new CookieHandler(_cookies, server())) { BaseAddress = Origin };

    public Task<HttpResponseMessage> GetAsync(string url) => Client.GetAsync(url, AdminHost.Token);

    public async Task<string> PageAsync(string url)
    {
        using var response = await GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET {url}");
        return await response.Content.ReadAsStringAsync(AdminHost.Token);
    }

    // The page's form with its hidden fields, the antiforgery token among them, and the fields given.
    public async Task<HttpResponseMessage> PostAsync(string url, string page, IReadOnlyDictionary<string, string> fields)
    {
        var form = new Dictionary<string, string>(HiddenFields(page), StringComparer.Ordinal);
        foreach (var (name, value) in fields)
        {
            form[name] = value;
        }

        using var content = new FormUrlEncodedContent(form);
        return await Client.PostAsync(url, content, AdminHost.Token);
    }

    // Follows the redirects from the URL until one leaves for the given base; a page that answers on the way goes to the
    // handler, which answers it as the user would.
    public async Task<Uri> FollowToAsync(string url, string stopAt, Func<HttpResponseMessage, Task<HttpResponseMessage>>? onPage = null)
    {
        var response = await GetAsync(url);
        for (var hops = 0; hops < 10; hops++)
        {
            if (response.StatusCode == HttpStatusCode.OK && onPage is not null)
            {
                response = await onPage(response);
                continue;
            }

            response.StatusCode.ShouldBe(HttpStatusCode.Redirect, $"{response.RequestMessage?.RequestUri}: {await response.Content.ReadAsStringAsync(AdminHost.Token)}");
            var location = response.Headers.Location!;
            var absolute = location.IsAbsoluteUri ? location : new Uri(Origin, location);
            response.Dispose();
            if (absolute.AbsoluteUri.StartsWith(stopAt, StringComparison.Ordinal))
            {
                return absolute;
            }

            response = await GetAsync(absolute.PathAndQuery);
        }

        throw new ShouldAssertException("Too many redirects.");
    }

    public void Dispose() => _client?.Dispose();

    private static CookieContainer Cookies(string? cookie)
    {
        var cookies = new CookieContainer();
        if (cookie is not null)
        {
            var (name, value) = (cookie[..cookie.IndexOf('=', StringComparison.Ordinal)], cookie[(cookie.IndexOf('=', StringComparison.Ordinal) + 1)..]);
            cookies.Add(Origin, new Cookie(name, value, "/"));
        }

        return cookies;
    }

    [GeneratedRegex(@"<input\b[^>]*>")]
    private static partial Regex Inputs();

    [GeneratedRegex(@"\b(type|name|value)=""([^""]*)""")]
    private static partial Regex Attributes();

    private static IEnumerable<KeyValuePair<string, string>> HiddenFields(string page)
    {
        foreach (Match input in Inputs().Matches(page))
        {
            var attributes = Attributes().Matches(input.Value).ToDictionary(match => match.Groups[1].Value, match => WebUtility.HtmlDecode(match.Groups[2].Value));
            if (attributes.GetValueOrDefault("type") == "hidden" && attributes.TryGetValue("name", out var name))
            {
                yield return KeyValuePair.Create(name, attributes.GetValueOrDefault("value") ?? "");
            }
        }
    }

    private sealed class CookieHandler(CookieContainer cookies, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = cookies.GetCookieHeader(uri);
            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    cookies.SetCookies(uri, value);
                }
            }

            return response;
        }
    }
}
