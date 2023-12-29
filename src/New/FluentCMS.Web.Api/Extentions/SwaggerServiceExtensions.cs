using Microsoft.AspNetCore.Builder;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Microsoft.Extensions.DependencyInjection;

public static class SwaggerServiceExtensions
{
    private static string _applicationName = string.Empty;
    private static string _applicationVersion = string.Empty;

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services, string applicationName = "FluentCMS API", string version = "v1.0.0")
    {
        _applicationName = applicationName;
        _applicationVersion = version;

        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(c =>
        {
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

            c.OrderActionsBy((apiDesc) => $"{apiDesc.ActionDescriptor.RouteValues["controller"]}");

            //// Include XML comments if available
            //var xmlFilename = $"{typeof(FluentCMS.Web.Api.Controllers.BaseController).Assembly.GetName().Name}.xml";
            //c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));

            //var xmlEntityFilename = $"{typeof(FluentCMS.Entities.AuditableEntity).Assembly.GetName().Name}.xml";
            //c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlEntityFilename));
        });

        return services;
    }

    public static IApplicationBuilder UseApiDocumentation(this IApplicationBuilder app)
    {
        // Enable middleware to serve generated Swagger as a JSON endpoint
        app.UseSwagger();

        // Enable middleware to serve Swagger UI
        app.UseSwaggerUI(c =>
        {
            c.DisplayRequestDuration();
            c.SwaggerEndpoint("/swagger/v1/swagger.json", _applicationName + " " + _applicationVersion);
            c.RoutePrefix = "api/doc";
            c.DocExpansion(DocExpansion.None);
        });

        return app;
    }
}
