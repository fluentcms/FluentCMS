using FluentCMS.Web.UI.ApiClients;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection;

public static class ClientServiceExtensions
{
    public static IServiceCollection AddApiClients(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddScoped<ApiHelper>();
        var assembly = Assembly.GetExecutingAssembly();
        var baseType = typeof(BaseClient);
        
        var derivedTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t) && t.Namespace == baseType.Namespace);

        foreach (var type in derivedTypes)
        {
            services.AddScoped(type);
        }
        return services;
    }
}
