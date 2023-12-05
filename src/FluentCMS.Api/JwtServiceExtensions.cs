using FluentCMS.Providers.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Microsoft.Extensions.DependencyInjection;

public static class JwtServiceExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfigurationManager configurationManager)
    {
        var options = configurationManager.GetInstance<JwtOptions>("Jwt") ??
            throw new ArgumentNullException("Jwt options in config file is not defined!");

        services.AddAuthentication(configureOptions =>
        {
            var defaultScheme = "Bearer";
            configureOptions.DefaultAuthenticateScheme = defaultScheme;
            configureOptions.DefaultScheme = defaultScheme;
            configureOptions.DefaultChallengeScheme = defaultScheme;
        })
            .AddCookie(cfg => cfg.SlidingExpiration = true)
            .AddJwtBearer(jwt =>
            {
                var key = Encoding.UTF8.GetBytes(options.Secret);
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
                    ValidAudience = options.Audience,
                    ValidIssuer = options.Issuer
                };
            });

        return services;
    }

}
