using FluentCMS.Infrastructure.Plugins.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace FluentCMS.Api.Plugins.OpenApiDocumentation;

[Plugin]
public class OpenApiPlugin : IPluginStartup
{
    public const string APPLICATION_NAME = "FluentCMS API";
    public const string APPLICATION_VERSION = "v1.0.0";

    public int ConfigureServicesPriority => 0;  // Ensure this runs early to set up identity
    public int ConfigurePriority => 0;          // Ensure this runs early to set up identity

    public void ConfigureServices(IServiceCollection services, IConfiguration? configuration)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = APPLICATION_NAME, Version = APPLICATION_VERSION });

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
        });
    }

    public void Configure(IApplicationBuilder app)
    {
        // Enable middleware to serve generated Swagger as a JSON endpoint
        app.UseSwagger();

        // Enable middleware to serve Swagger UI
        app.UseSwaggerUI(c =>
        {
            c.DisplayRequestDuration();
            c.SwaggerEndpoint("/swagger/v1/swagger.json", APPLICATION_NAME + " " + APPLICATION_VERSION);
            c.RoutePrefix = "api/doc";
            c.DocExpansion(DocExpansion.None);
        });
    }
}
