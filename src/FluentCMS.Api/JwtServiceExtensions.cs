using FluentCMS.Providers.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for setting up JWT (JSON Web Token) authentication services in an <see cref="IServiceCollection" />.
/// </summary>
public static class JwtServiceExtensions
{
    /// <summary>
    /// Adds JWT authentication services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configurationManager">The configuration manager to read JWT options from.</param>
    /// <returns>The updated <see cref="IServiceCollection" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when JWT options are not defined in the configuration.</exception>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfigurationManager configurationManager)
    {
        // Retrieve JWT options from configuration or throw an exception if not found
        var options = configurationManager.GetInstance<JwtOptions>("Jwt") ??
            throw new ArgumentNullException("Jwt options in config file is not defined!");

        // Configure the authentication services
        services.AddAuthentication(configureOptions =>
        {
            var defaultScheme = "Bearer";
            configureOptions.DefaultAuthenticateScheme = defaultScheme;
            configureOptions.DefaultScheme = defaultScheme;
            configureOptions.DefaultChallengeScheme = defaultScheme;
        })
        .AddJwtBearer(jwt =>
        {
            // Encoding the secret key
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
