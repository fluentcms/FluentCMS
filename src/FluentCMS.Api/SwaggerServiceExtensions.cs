using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Microsoft.Extensions.DependencyInjection;

public static class SwaggerServiceExtensions
{
    private static string _applicationName = string.Empty;
    private static string _applicationVersion = string.Empty;

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services, string applicationName = "FluentCMS API", string version = "v.1.0.0")
    {
        _applicationName = applicationName;
        _applicationVersion = version;

        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(c =>
        {
            c.OrderActionsBy(x => x.RelativePath);
            c.SwaggerDoc("v1", new OpenApiInfo { Title = applicationName, Version = version });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

        });

        return services;
    }

    public static IApplicationBuilder UseApiDocumentation(this IApplicationBuilder app, string routePrefix = "doc")
    {
        // Enable middleware to serve generated Swagger as a JSON endpoint.
        app.UseSwagger();

        // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
        // specifying the Swagger JSON endpoint.
        app.UseSwaggerUI(c =>
        {
            c.DisplayRequestDuration();
            c.SwaggerEndpoint("/swagger/v1/swagger.json", _applicationName + ", Version " + _applicationVersion);
            c.RoutePrefix = routePrefix;
            c.DocExpansion(DocExpansion.None);
        });

        return app;
    }
}
