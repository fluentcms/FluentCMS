using FluentCMS.Repositories.MongoDB;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Microsoft.Extensions.DependencyInjection;

public static class MongoDbServiceExtensions
{
    public static IServiceCollection AddMongoDbRepositories(this IServiceCollection services, string connectionString)
    {
        // register default GUID serializer for MongoDB
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        BsonDefaults.GuidRepresentation = GuidRepresentation.Standard;

        // Register MongoDB context and options
        services.AddSingleton(provider => CreateMongoDBOptions(provider, connectionString));
        services.AddSingleton<IMongoDBContext, MongoDBContext>();

        // Register all repositories using reflection
        RegisterRepositories(services);

        return services;
    }

    private static MongoDBOptions<MongoDBContext> CreateMongoDBOptions(IServiceProvider provider, string connectionString)
    {
        var configuration = provider.GetService<IConfiguration>() ?? throw new InvalidOperationException("IConfiguration is not registered.");
        var connString = configuration.GetConnectionString(connectionString);
        return connString is not null
            ? new MongoDBOptions<MongoDBContext>(connString)
            : throw new InvalidOperationException($"Connection string '{connectionString}' not found.");
    }

    private static void RegisterRepositories(IServiceCollection services)
    {
        var repositoryTypes = typeof(MongoDbServiceExtensions).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Repository"))
            .ToList();

        foreach (var repositoryType in repositoryTypes)
        {
            var interfaceType = repositoryType.GetInterfaces().FirstOrDefault(i => i.Name.EndsWith(repositoryType.Name))
                ?? throw new InvalidOperationException($"Interface for repository '{repositoryType.Name}' not found.");

            services.AddScoped(interfaceType, repositoryType);
        }
    }
}
