using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Headers;

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
            services.AddSingleton<IHttpClientFactory>(new E2eHttpClientFactory<Program>(this));
        });
    }
}
