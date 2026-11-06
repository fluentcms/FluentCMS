using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Shouldly;
using Xunit;

namespace FluentCMS.Host.Tests;

// What a browser does with the hosted pages: it keeps cookies, follows no redirect on its own, and posts a page's form
// with the antiforgery token the page rendered.
internal sealed partial class Browser : IDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _client;

    public Browser(TestServer server)
    {
        _client = new HttpClient(new CookieHandler(_cookies, server.CreateHandler())) { BaseAddress = HostUnderTest.Https };
    }

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
        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = AntiforgeryToken(page) };
        using var content = new FormUrlEncodedContent(form);
        return await _client.PostAsync(url, content, Token);
    }

    // Follows redirects inside the host until the flow leaves for a client's redirect URI.
    public async Task<HttpResponseMessage> FollowAsync(HttpResponseMessage response, string stopAt)
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
