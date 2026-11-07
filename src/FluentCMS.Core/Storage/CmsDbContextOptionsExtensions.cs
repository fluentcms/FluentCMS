using FluentCMS.Core.Plugins;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Core.Storage;

public static class CmsDbContextOptionsExtensions
{
    private static readonly SchemaModel NoContent = new SchemaBuilder().Build();

    // Gives CmsDbContext the schema to build its model from. Without conventions the model stays neutral, and
    // without a dialect it has no JSON functions; a data provider package passes its own.
    public static TBuilder UseCmsModel<TBuilder>(this TBuilder builder, SchemaModel schema, IStorageConventions? conventions = null, IJsonSqlDialect? jsonDialect = null)
        where TBuilder : DbContextOptionsBuilder =>
        builder.UseCmsModel(schema, conventions, jsonDialect, plugins: null);

    internal static TBuilder UseCmsModel<TBuilder>(this TBuilder builder, SchemaModel schema, IStorageConventions? conventions, IJsonSqlDialect? jsonDialect, PluginModels? plugins)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schema);

        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new CmsModelOptionsExtension(schema, conventions ?? new StorageConventions(), jsonDialect, plugins));
        builder.ReplaceService<IModelCacheKeyFactory, CmsModelCacheKeyFactory>();
        return builder;
    }

    // Gives SystemDbContext the conventions to build its model with.
    public static TBuilder UseSystemModel<TBuilder>(this TBuilder builder, IStorageConventions conventions)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(conventions);

        return builder.UseCmsModel(NoContent, conventions);
    }
}
