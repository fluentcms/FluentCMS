using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Core.Storage;

public static class CmsDbContextOptionsExtensions
{
    // Gives CmsDbContext the schema to build its model from. Without conventions the model stays neutral;
    // a data provider package passes its own.
    public static TBuilder UseCmsModel<TBuilder>(this TBuilder builder, SchemaModel schema, IStorageConventions? conventions = null)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schema);

        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new CmsModelOptionsExtension(schema, conventions ?? new StorageConventions()));
        builder.ReplaceService<IModelCacheKeyFactory, CmsModelCacheKeyFactory>();
        return builder;
    }
}
