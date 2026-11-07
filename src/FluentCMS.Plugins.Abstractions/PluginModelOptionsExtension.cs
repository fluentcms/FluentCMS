using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins;

// The key of a plugin model in the model cache, so that two hosts with other conventions or contributors get their own.
internal interface IPluginModelKey
{
    string ModelKey { get; }
}

// What core gives a PluginDbContext through its options.
internal sealed class PluginModelOptionsExtension(
    IReadOnlyList<IPluginModelContributor> contributors,
    Action<ModelConfigurationBuilder>? conventions,
    string key) : IDbContextOptionsExtension
{
    private ExtensionInfo? _info;

    public IReadOnlyList<IPluginModelContributor> Contributors { get; } = contributors;

    public Action<ModelConfigurationBuilder>? Conventions { get; } = conventions;

    public string Key { get; } = key;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public static void Use(DbContextOptionsBuilder options, IReadOnlyList<IPluginModelContributor> contributors, Action<ModelConfigurationBuilder>? conventions, string key) =>
        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(new PluginModelOptionsExtension(contributors, conventions, key));

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    // Every plugin model shares one EF service provider, and the model cache key tells the models apart.
    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using a FluentCMS plugin model ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
        }
    }
}
