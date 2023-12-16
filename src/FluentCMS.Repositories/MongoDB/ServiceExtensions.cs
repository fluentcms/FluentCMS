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
        if (BsonSerializer.LookupSerializer<decimal>() == null)
            BsonSerializer.RegisterSerializer(typeof(decimal), new DecimalSerializer(BsonType.Decimal128));

        if (BsonSerializer.LookupSerializer<decimal?>() == null)
            BsonSerializer.RegisterSerializer(typeof(decimal?), new NullableSerializer<decimal>(new DecimalSerializer(BsonType.Decimal128)));

        if (BsonSerializer.LookupSerializer<Guid>() == null)
            BsonSerializer.RegisterSerializer(typeof(Guid), new GuidSerializer(GuidRepresentation.Standard));

        // TODO: if we remove this line, Id queries will not work
        BsonDefaults.GuidRepresentation = GuidRepresentation.Standard;

        services.AddSingleton(provider =>
        {
            var configuration = provider.GetService<IConfiguration>() ?? throw new InvalidOperationException("IConfiguration is not registered.");

            var connString = configuration.GetConnectionString(connectionString);

            return connString is null
                ? throw new InvalidOperationException($"Connection string '{connectionString}' not found.")
                : new MongoDBOptions<MongoDBContext>(connString);
        });

        services.AddSingleton<IMongoDBContext, MongoDBContext>();

        // using reflection to register all repositories
        var repositoryTypes = typeof(MongoDbServiceExtensions).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Repository"))
            .ToList();

        foreach (var repositoryType in repositoryTypes)
        {
            var interfaceType = repositoryType.GetInterfaces().FirstOrDefault(i => i.Name.EndsWith(repositoryType.Name))
                ?? throw new InvalidOperationException($"Interface for repository '{repositoryType.Name}' not found.");

            services.AddScoped(interfaceType, repositoryType);
        }

        return services;
    }
}
