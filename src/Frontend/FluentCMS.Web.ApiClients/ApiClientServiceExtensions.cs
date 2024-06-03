using FluentCMS.Web.ApiClients;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;

namespace Microsoft.Extensions.DependencyInjection;

public static class ApiClientServiceExtensions
{
    public static IServiceCollection AddApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(HttpClientFactoryHelper.HTTP_CLIENT_API_NAME, (sp, client) =>
        {
            string apiServer = configuration?["ApiServer"] ??
                throw new ArgumentNullException("ApiServer is not configured in appsettings.json");

            client.BaseAddress = new Uri(apiServer);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        });
        return services;
    }
}
