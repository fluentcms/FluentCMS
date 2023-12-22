using FluentCMS;
using FluentCMS.Web.Api;

namespace Microsoft.Extensions.DependencyInjection;

public static class ApiServiceExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddControllers();

        services.AddAuthentication();

        services.AddAuthorization();

        services.AddHttpContextAccessor();

        services.AddScoped<IAuthContext, AuthContext>();

        services.AddScoped<SetupManager>();

        return services;
    }
}
