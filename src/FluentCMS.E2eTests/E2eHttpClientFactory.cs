using Microsoft.AspNetCore.Mvc.Testing;

namespace FluentCMS.E2eTests;

internal class E2eHttpClientFactory<TStartup> : IHttpClientFactory
    where TStartup : class
{
    private readonly WebApplicationFactory<TStartup> _appFactory;

    public E2eHttpClientFactory(WebApplicationFactory<TStartup> appFactory) => _appFactory = appFactory;

    public HttpClient CreateClient(string name) => _appFactory.CreateClient();
}
