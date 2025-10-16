using FluentCMS.Api.Core.Api;
using FluentCMS.Api.Core.Api.Filters;
using FluentCMS.Api.Core.Api.Middlewares;
using Microsoft.AspNetCore.Builder;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerUI;
using System.Text.Json.Serialization;

namespace FluentCMS.Api.Core.Api;

public static class ServiceCollectionExtensions
{
    public const string SESSION_ID_HEADER_KEY = "X_Session_Id";
    public const string UNIQUE_USER_ID_HEADER_KEY = "X-Unique-Id";
    public const string DEFAULT_LANGUAGE = "en-US";
    public const string USER_IP_FORWARDED_HEADER_KEY = "X-Forwarded-For";

    public static IServiceCollection AddSecurityContext(this IServiceCollection services)
    {
        // Replace the default role validator with our site-scoped one
        services.AddScoped<IRoleValidator<Role>, EnhancedSiteScopedRoleValidator>();
        services.AddScoped<SecurityContextResolver>();
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext>(sp => sp.GetRequiredService<ISecurityContext>());
        services.AddScoped(sp => sp.GetRequiredService<SecurityContextResolver>().Resolve());
        return services;
    }

    public static IServiceCollection AddFluentCmsApi(this IServiceCollection services)
    {
        services.AddSecurityContext();

        services.AddScoped<IApiTokenValidator, ApiTokenValidator>();

        services.AddEndpointsApiExplorer();

        services.AddAuthorization();

        //services.AddApplicationServices();
        services.AddOptions<JwtOptions>()
            .BindConfiguration("JwtOptions");

        services
            .AddControllers(config =>
            {
                config.Filters.Add<ApiResultValidateModelFilter>();
                config.Filters.Add<ApiResultExceptionFilter>();
                config.Filters.Add<ApiResultActionFilter>();
            })
            .AddJsonOptions(options =>
            {
                //options.JsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                // This is already enabled by default with [ApiController]
                options.SuppressModelStateInvalidFilter = true;
            });


        services.AddApiDocumentation();
        return services;
    }


    public static WebApplication UseFluentCmsApi(this WebApplication app)
    {
        app.UseApiDocumentation();

        app.UseAuthentication();

        app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), app =>
        {
            // this will be executed only when the path starts with "/api"
            app.UseMiddleware<JwtAuthorizationMiddleware>();
        });

        app.UseAuthorization();

        app.MapControllers();

        return app;
    }

    private static string _applicationName = string.Empty;
    private static string _applicationVersion = string.Empty;

    private static IServiceCollection AddApiDocumentation(this IServiceCollection services, string applicationName = "FluentCMS API", string version = "v1.0.0")
    {
        _applicationName = applicationName;
        _applicationVersion = version;


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
        });

        return services;
    }

    private static IApplicationBuilder UseApiDocumentation(this IApplicationBuilder app)
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
