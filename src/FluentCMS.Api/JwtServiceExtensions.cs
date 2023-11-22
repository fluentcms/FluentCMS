using FluentCMS.Providers.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Api;

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
