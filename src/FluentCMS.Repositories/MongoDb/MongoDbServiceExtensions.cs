using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.MongoDb;
using Microsoft.Extensions.Configuration;
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

        // TODO: if we remove this line, Id queries will not work
        BsonDefaults.GuidRepresentation = GuidRepresentation.Standard;

        services.AddSingleton(provider =>
        {
            var configuration = provider.GetService<IConfiguration>() ?? throw new InvalidOperationException("IConfiguration is not registered.");

            var connString = configuration.GetConnectionString(connectionString);

            return connString is null
                ? throw new InvalidOperationException($"Connection string '{connectionString}' not found.")
                : new MongoDbOptions<MongoDbContext>(connString);
        });

        services.AddSingleton<IMongoDBContext, MongoDbContext>();

        services.AddScoped(typeof(IGenericRepository<>), typeof(MongoDbGenericRepository<>));

        services.AddScoped<ISiteRepository, MongoDbSiteRepository>();
        services.AddScoped<IPageRepository, MongoDbPageRepository>();
        services.AddScoped<IHostRepository, MongoDbHostRepository>();

        services.AddMongoDbIdentityRepositories();

        return services;
    }
}
