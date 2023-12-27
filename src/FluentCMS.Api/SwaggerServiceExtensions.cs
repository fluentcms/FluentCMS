using Microsoft.AspNetCore.Builder;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for setting up Swagger services in an <see cref="IServiceCollection"/> and configuring Swagger middleware in an <see cref="IApplicationBuilder"/>.
/// </summary>
public static class SwaggerServiceExtensions
{
    private static string _applicationName = string.Empty;
    private static string _applicationVersion = string.Empty;

    /// <summary>
    /// Adds Swagger generation services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="applicationName">The name of the application to display in Swagger UI. Defaults to "FluentCMS API".</param>
    /// <param name="version">The version of the application to display in Swagger UI. Defaults to "v1.0.0".</param>
    /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services, string applicationName = "FluentCMS API", string version = "v1.0.0")
    {
        _applicationName = applicationName;
        _applicationVersion = version;

        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(c =>
        {
            c.OrderActionsBy(x => x.RelativePath);
            c.SwaggerDoc("v1", new OpenApiInfo { Title = applicationName, Version = version });

            // Define the security scheme for bearer tokens
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            // Include XML comments if available
            var xmlFilename = $"{typeof(FluentCMS.Api.Controllers.BaseController).Assembly.GetName().Name}.xml";
            c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));

            var xmlEntityFilename = $"{typeof(FluentCMS.Entities.AuditEntity).Assembly.GetName().Name}.xml";
            c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlEntityFilename));
        });

        return services;
    }

    /// <summary>
    /// Configures the <see cref="IApplicationBuilder"/> to use Swagger UI and Swagger JSON endpoint.
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> to configure.</param>
    /// <param name="routePrefix">The route prefix for accessing Swagger UI. Defaults to "doc".</param>
    /// <returns>The configured <see cref="IApplicationBuilder"/>.</returns>
    public static IApplicationBuilder UseApiDocumentation(this IApplicationBuilder app, string routePrefix = "doc")
    {
        // Enable middleware to serve generated Swagger as a JSON endpoint
        app.UseSwagger();

        // Enable middleware to serve Swagger UI
        app.UseSwaggerUI(c =>
        {
            c.DisplayRequestDuration();
            c.SwaggerEndpoint("/swagger/v1/swagger.json", _applicationName + " " + _applicationVersion);
            c.RoutePrefix = routePrefix;
            c.DocExpansion(DocExpansion.None);
        });

        return app;
    }
}
