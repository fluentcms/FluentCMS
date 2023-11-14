using FluentCMS.Providers.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Microsoft.Extensions.DependencyInjection;

public static class UserTokenServiceExtensions
{
    public static AuthenticationBuilder AddBearerJwtAddAuthentication(this AuthenticationBuilder builder, IConfiguration configuration)
    {

        builder.AddJwtBearer(jwt =>
        {
            var jwtOptions = configuration.GetSection("Jwt").Get<JwtOptions>();
            var key = Encoding.ASCII.GetBytes(jwtOptions.Secret);
            jwt.SaveToken = true;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidateAudience = true,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                SaveSigninToken = true,
                ValidAudience = jwtOptions.Audience,
                ValidIssuer = jwtOptions.Issuer
            };

        });
        return builder;
    }
    public static AuthorizationOptions AddJwtAuthorization(this AuthorizationOptions options, IConfiguration configuration)
    {
        options.AddPolicy(JwtAuthorizationDefaults.JwtAuthorizationPolicyName, p =>
        {
            p.RequireAuthenticatedUser();
            p.AddAuthenticationSchemes("Bearer");
        });
        return options;
    }
    public static IServiceCollection AddJwtTokenProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.AddScoped<IUserTokenProvider, JwtUserTokenProvider>();

        return services;
    }
}
