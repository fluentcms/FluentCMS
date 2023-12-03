using FluentCMS.Entities;
using FluentCMS.Repositories;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.MongoDb;
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

        //BsonSerializer.RegisterSerializer(new ContentSerializer());

        //BsonClassMap.RegisterClassMap<Content>(cm =>
        //{
        //    cm.AutoMap();
        //    cm.MapProperty(c => c.Id);
        //    cm.MapProperty(c => c.CreatedBy);
        //    cm.MapProperty(c => c.CreatedAt);
        //    cm.MapProperty(c => c.LastUpdatedBy);
        //    cm.MapProperty(c => c.LastUpdatedAt);
        //    cm.MapProperty(c => c.TypeId);
        //    cm.MapProperty(c => c.SiteId);
        //    cm.MapCreator(c => new Content());
        //});

        services.AddSingleton(provider =>
        {
            var configuration = provider.GetService<IConfiguration>() ?? throw new InvalidOperationException("IConfiguration is not registered.");

            var connString = configuration.GetConnectionString(connectionString);

            return connString is null
                ? throw new InvalidOperationException($"Connection string '{connectionString}' not found.")
                : new MongoDBOptions<MongoDBContext>(connString);
        });

        services.AddSingleton<IMongoDBContext, MongoDBContext>();

        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<IPageRepository, PageRepository>();
        services.AddScoped<IHostRepository, HostRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IPluginDefinitionRepository, PluginDefinitionRepository>();
        services.AddScoped<IPluginRepository, PluginRepository>();
        services.AddScoped<ILayoutRepository, LayoutRepository>();
        services.AddScoped<IContentRepository, ContentRepository>();

        return services;
    }
}
