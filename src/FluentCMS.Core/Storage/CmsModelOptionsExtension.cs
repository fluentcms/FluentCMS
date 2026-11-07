using FluentCMS.Core.Plugins;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Storage;

// Carries the schema to CmsDbContext through its options, so the context has the one constructor that
// AddDbContext, pooling and design-time factories expect.
internal sealed class CmsModelOptionsExtension(SchemaModel schema, IStorageConventions conventions, IJsonSqlDialect? jsonDialect, PluginModels? plugins = null) : IDbContextOptionsExtension
{
    private ExtensionInfo? _info;

    // The plugins' tables, which CmsDbContext maps and leaves to the plugins' own streams.
    public PluginModels Plugins { get; } = plugins ?? PluginModels.None;

    public SchemaModel Schema { get; } = schema;

    public IStorageConventions Conventions { get; } = conventions;

    public IJsonSqlDialect? JsonDialect { get; } = jsonDialect;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    // The schema is left out of the service provider identity: every schema shares one EF service
    // provider and its model cache, and CmsModelCacheKeyFactory tells the models apart.
    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using FluentCMS schema ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
        }
    }
}
