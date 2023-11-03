using FluentCMS.Repositories.MongoDb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Microsoft.Extensions.DependencyInjection;

public static class MongoDbServiceExtensions
{
    public static IServiceCollection AddMongoDbRepositories(this IServiceCollection services, string connectionString)
    {
        BsonSerializer.RegisterSerializer(typeof(decimal), new DecimalSerializer(BsonType.Decimal128));
        BsonSerializer.RegisterSerializer(typeof(decimal?), new NullableSerializer<decimal>(new DecimalSerializer(BsonType.Decimal128)));
        BsonSerializer.RegisterSerializer(typeof(Guid), new GuidSerializer(GuidRepresentation.Standard));

        services.TryAddSingleton(provider =>
        {
            var configuration = provider.GetService<IConfiguration>() ?? throw new InvalidOperationException("IConfiguration is not registered.");

            var connString = configuration.GetConnectionString(connectionString);

            return connString is null
                ? throw new InvalidOperationException($"Connection string '{connectionString}' not found.")
                : new MongoDbOptions<MongoDbContext>(connString);
        });

        services.TryAddSingleton<MongoDbContext>();
        services.TryAddSingleton<IMongoDBContext, MongoDbContext>();

        return services;
    }
}
