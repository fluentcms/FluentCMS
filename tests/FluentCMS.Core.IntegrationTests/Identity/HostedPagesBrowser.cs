using System.Net;
using System.Text.RegularExpressions;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// What a browser does with the hosted pages: it keeps cookies, comes from one client address, follows no redirect on its
// own, and posts a page's form with the antiforgery token the page rendered.
internal sealed partial class HostedPagesBrowser : IDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _client;

    // The base address is the host the requests name, https://localhost unless a test forges another.
    public HostedPagesBrowser(FluentCmsTestHost host, string address = "10.1.0.1", Uri? baseAddress = null)
    {
        var server = (TestServer)host.Services.GetRequiredService<IServer>();
        var handler = server.CreateHandler(context => context.Connection.RemoteIpAddress = IPAddress.Parse(address));
        _client = new HttpClient(new CookieHandler(_cookies, handler)) { BaseAddress = baseAddress ?? OAuthBase };
    }

    public static Uri OAuthBase { get; } = new("https://localhost/");

    public bool HasCookie(string name) => _cookies.GetCookies(_client.BaseAddress!).Any(cookie => cookie.Name == name && !cookie.Expired);

    public Task<HttpResponseMessage> GetAsync(string url) => _client.GetAsync(url, Token);

    public async Task<string> PageAsync(string url)
    {
        using var response = await GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET {url}");
        return await response.Content.ReadAsStringAsync(Token);
    }

    // Opens the page and posts its form back to the same URL, as its submit button does.
    public async Task<HttpResponseMessage> SubmitAsync(string url, IReadOnlyDictionary<string, string> fields)
    {
        var page = await PageAsync(url);
        return await PostAsync(url, page, fields);
    }

    public Task<HttpResponseMessage> PostAsync(string url, string page, IReadOnlyDictionary<string, string> fields)
    {
        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = AntiforgeryToken(page) };
        return PostAsync(url, form);
    }

    public async Task<HttpResponseMessage> PostAsync(string url, IReadOnlyDictionary<string, string> form)
    {
        using var content = new FormUrlEncodedContent(form);
        return await _client.PostAsync(url, content, Token);
    }

    // A request that is not a form, such as a JSON body, with the browser's cookies and address.
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        return await _client.SendAsync(request, Token);
    }

    // Follows redirects until a page answers or the flow leaves for the client's redirect URI.
    public async Task<HttpResponseMessage> FollowAsync(HttpResponseMessage response, string stopAt = FluentCmsTestHost.OAuthRedirectUri)
    {
        for (var hops = 0; hops < 10; hops++)
        {
            if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location is not { } location)
            {
                return response;
            }

            if (location.IsAbsoluteUri && location.AbsoluteUri.StartsWith(stopAt, StringComparison.Ordinal))
            {
                return response;
            }

            var next = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
            response.Dispose();
            response = await GetAsync(next);
        }

        throw new ShouldAssertException("Too many redirects.");
    }

    public void Dispose() => _client.Dispose();

    private static string AntiforgeryToken(string page) =>
        Antiforgery().Match(page) is { Success: true } match
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new ShouldAssertException("The page has no antiforgery token.");

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex Antiforgery();

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
