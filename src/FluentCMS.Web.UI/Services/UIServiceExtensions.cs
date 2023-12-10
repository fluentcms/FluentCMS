using FluentCMS.Web.UI;
using System.Net.Http.Headers;

namespace Microsoft.Extensions.DependencyInjection;

public static class UIServiceExtensions
{
    public static IServiceCollection AddUIServices(this IServiceCollection services)
    {
        services.AddScoped<AppStateService>();

        services.AddApiClients();

        services.AddHttpClient("FluentCMS.Web.Api", client =>
        {
            // TODO: Move this to configuration
            client.BaseAddress = new Uri("https://localhost:7164/api/");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // Add services to the container.
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        return services;
    }
}
