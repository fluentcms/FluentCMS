using FluentCMS.Providers.Identity;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class UserTokenServiceExtensions
{
    public static IServiceCollection AddJwtTokenProvider(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection("Jwt"));

        services.AddScoped<IUserTokenProvider, JwtUserTokenProvider>();

        return services;
    }
}
