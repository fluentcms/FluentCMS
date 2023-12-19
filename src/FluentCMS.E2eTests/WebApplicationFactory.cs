using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.E2eTests;
internal class WebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            // replace HttpClientFactory with E2eHttpClientFactory
            var httpClientFactoryDescriptor = services.First(x => x.ServiceType == typeof(IHttpClientFactory));
            services.Remove(httpClientFactoryDescriptor);
            services.AddTransient<IHttpClientFactory>(_=>new E2eHttpClientFactory<Program>(this));
        });
    }
    protected override void ConfigureClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        base.ConfigureClient(client);
    }
}
