﻿using FluentCMS;
using FluentCMS.Web.Api;
using FluentCMS.Web.Api.Filters;
using FluentCMS.Web.Api.Middleware;
using FluentCMS.Web.Api.Setup;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Extensions.DependencyInjection;

public static class ApiServiceExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddApplicationServices();

        services.AddControllers(config =>
        {
            config.Filters.Add<ApiResultActionFilter>();
            config.Filters.Add<ApiResultExceptionFilter>();
        });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer((c) =>
            {
                var serviceProvider = services.BuildServiceProvider();
                var options = serviceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;
                var key = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(options.Secret));
                c.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    SaveSigninToken = true,
                    ValidAudience = options.Audience,
                    ValidIssuer = options.Issuer
                };
            });

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

        app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), app =>
        {
            // this will be executed only when the path starts with "/api"
            app.UseMiddleware<ApplicationExecutionContextMiddleware>();
        });

        app.UseAuthentication();

        app.UseAuthorization();

        app.MapControllers();

        return app;
    }
}
