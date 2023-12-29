using FluentCMS;
using FluentCMS.Web.Api;
using FluentCMS.Web.Api.Filters;
using FluentCMS.Web.Api.Setup;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.Extensions.DependencyInjection;

public static class ApiServiceExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddApplicationServices();

        services.AddControllers(config =>
        {
            config.Filters.Add<ApiResultActionFilter>();
        });

        services.AddAuthentication();

        services.AddAuthorization();

        services.AddHttpContextAccessor();

        services.AddScoped<ApiExecutionContext>();

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

        //app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), app =>
        //{
        //    // this will be executed only when the path starts with "/api"
        //    app.UseMiddleware<ApiExecutionContextHandlerMiddleware>();
        //});

        app.UseAuthentication();

        app.UseAuthorization();

        app.MapControllers();

        return app;
    }
}
