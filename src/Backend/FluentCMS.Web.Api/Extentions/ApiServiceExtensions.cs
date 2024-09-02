using FluentCMS;
using FluentCMS.Web.Api;
using FluentCMS.Web.Api.Filters;
using FluentCMS.Web.Api.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.DependencyInjection;

public static class ApiServiceExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddApplicationServices();

        services
            .AddControllers(config =>
            {
                config.Filters.Add<ApiResultActionFilter>();
                config.Filters.Add<ApiResultExceptionFilter>();
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = (context) =>
                {
                    var apiExecutionContext = services.BuildServiceProvider().GetRequiredService<IApiExecutionContext>();
                    var apiResult = new ApiResult<object>
                    {
                        Duration = (DateTime.UtcNow - apiExecutionContext.StartDate).TotalMilliseconds,
                        SessionId = apiExecutionContext.SessionId,
                        TraceId = apiExecutionContext.TraceId,
                        UniqueId = apiExecutionContext.UniqueId,
                        Status = 400,
                        IsSuccess = false
                    };

                    foreach (var item in context.ModelState)
                    {
                        var errors = item.Value.Errors;
                        if (errors?.Count > 0)
                        {
                            foreach (var error in errors)
                            {
                                apiResult.Errors.Add(new AppError { Code = item.Key, Description = error.ErrorMessage });
                            }
                        }
                    }

                    return new BadRequestObjectResult(apiResult);
                };
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

        services.AddScoped<IApiExecutionContext>(sp => new ApiExecutionContext(sp.GetRequiredService<IHttpContextAccessor>()));

        services.AddAutoMapper(typeof(ApiServiceExtensions));

        services.AddApiDocumentation();

        return services;
    }

    public static WebApplication UseApiService(this WebApplication app)
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
}
