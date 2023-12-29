using FluentCMS;
using FluentCMS.Web.Api;
using FluentCMS.Web.Api.Middleware;
using FluentCMS.Web.Api.Setup;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.Extensions.DependencyInjection;

public static class ApiServiceExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddApplicationServices();

        services.AddControllers();

        services.AddAuthentication();

        services.AddAuthorization();

        services.AddHttpContextAccessor();

        services.AddScoped<IAuthContext, AuthContext>();

        services.AddScoped<SetupManager>();

        services.AddAutoMapper(typeof(ApiServiceExtensions));

        services.AddApiDocumentation();

        return services;
    }

    public static WebApplication UseApiService(this WebApplication app)
    {
        app.UseApiDocumentation();

        app.UseAntiforgery();

        app.UseAuthentication();

        app.UseAuthorization();

        app.UseMiddleware<ApiResultHandlerMiddleware>();

        app.MapControllers();

        return app;
    }
}
