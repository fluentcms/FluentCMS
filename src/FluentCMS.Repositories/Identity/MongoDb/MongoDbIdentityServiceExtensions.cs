using FluentCMS.Repositories.Identity.Abstractions;
using FluentCMS.Repositories.Identity.MongoDb;

namespace Microsoft.Extensions.DependencyInjection;

public static class MongoDbIdentityServiceExtensions
{
    public static IServiceCollection AddMongoDbIdentityRepositories(this IServiceCollection services)
    {

        services.AddScoped<IUserRepository, MongoDbUserRepository>();
        services.AddScoped<IRoleRepository, MongoDbRoleRepository>();
        services.AddScoped<IUserTokenRepository, MongoDbUserTokenRepository>();

        return services;
    }
}
