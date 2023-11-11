using FluentCMS.Providers.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class UserTokenServiceExtensions
{
    public static IServiceCollection AddJwtTokenProvider(this IServiceCollection services)
    {
        services.AddScoped<IUserTokenProvider, JwtUserTokenProvider>();

        return services;
    }
}
