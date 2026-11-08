using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Plugins;

// One plugin assembly holds a migration set per provider. EF finds the migrations of a context by its type, which every
// provider's set shares, so a data provider names its set in ConfigurePluginMigrations: the migrations and the snapshot whose
// namespace ends in that name, such as FluentCMS.Plugins.Webhooks.Migrations.PostgreSql, which is what
// `dotnet ef migrations add --output-dir Migrations/PostgreSql` writes. See docs/plugin-migrations.md.
public static class PluginMigrationSets
{
    public const string Sqlite = "Sqlite";
    public const string PostgreSql = "PostgreSql";
    public const string SqlServer = "SqlServer";

    // An assembly with no set under any of these names holds one set, as before per-provider sets, which every provider
    // takes and the foreign-set check refuses on a provider it was not scaffolded for.
    internal static readonly IReadOnlyList<string> Names = [Sqlite, PostgreSql, SqlServer];

    public static TBuilder UsePluginMigrationSet<TBuilder>(this TBuilder builder, string set)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(set);

        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new PluginMigrationSetExtension(set));
        builder.ReplaceService<IMigrationsAssembly, PluginMigrationsAssembly>();
        return builder;
    }
}

internal sealed class PluginMigrationSetExtension(string set) : IDbContextOptionsExtension
{
    private ExtensionInfo? _info;

    public string Set { get; } = set;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    // PluginMigrationsAssembly reads the set from the options of each context, so every set shares one EF service provider.
    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => $"using the {((PluginMigrationSetExtension)Extension).Set} plugin migration set ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
        }
    }
}
