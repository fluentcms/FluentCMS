using FluentCMS.Web.UI.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class AdminUIServiceExtensions
{
    public static IServiceCollection AddAdminUIServices(this IServiceCollection services)
    {
        services.AddApiClients();
        services.AddScoped<SetupManager>();

        return services;
    }
}
