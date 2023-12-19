using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Headers;

namespace FluentCMS.E2eTests;

internal class E2eHttpClientFactory<TStartup> : IHttpClientFactory
    where TStartup : class
{
    private readonly WebApplicationFactory<TStartup> _appFactory;

    public E2eHttpClientFactory(WebApplicationFactory<TStartup> appFactory) => _appFactory = appFactory;
    public static string? OverrideToken { get; set; }
    public HttpClient CreateClient(string name)
    {
        var client = _appFactory.CreateClient();
        if (!string.IsNullOrEmpty(OverrideToken))
        {
            client
                .DefaultRequestHeaders
                .Authorization = new AuthenticationHeaderValue("Bearer",
                    OverrideToken);
        }
        return client;
    }
}
