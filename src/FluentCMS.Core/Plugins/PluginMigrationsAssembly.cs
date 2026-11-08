using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FluentCMS.Core.Plugins;

// EF's migrations of a plugin's context, kept to the set its options name (PluginMigrationSets). Runtime, the foreign-set
// check and `dotnet ef` all read the migrations through this, so each sees the running provider's set only.
internal sealed class PluginMigrationsAssembly : IMigrationsAssembly
{
    private readonly Type _contextType;
    private readonly IMigrationsIdGenerator _ids;
    private readonly Lazy<Contents> _contents;

    public PluginMigrationsAssembly(ICurrentDbContext currentContext, IDbContextOptions options, IMigrationsIdGenerator ids)
    {
        _contextType = currentContext.Context.GetType();
        _ids = ids;
        Set = options.FindExtension<PluginMigrationSetExtension>()!.Set;

        var relational = RelationalOptionsExtension.Extract(options);
        Assembly = relational.MigrationsAssemblyObject
            ?? (relational.MigrationsAssembly is { } name ? Assembly.Load(new AssemblyName(name)) : _contextType.Assembly);
        _contents = new(Load);
    }

    public string Set { get; }

    // Whether the assembly holds a set under the name of any provider. Without one, its migrations are a single set.
    public bool PerProvider => _contents.Value.PerProvider;

    public bool HasSet => _contents.Value.HasSet;

    public Assembly Assembly { get; }

    public IReadOnlyDictionary<string, TypeInfo> Migrations => _contents.Value.Migrations;

    public ModelSnapshot? ModelSnapshot => _contents.Value.Snapshot;

    public string? FindMigrationId(string nameOrId) =>
        Migrations.Keys.FirstOrDefault(_ids.IsValidId(nameOrId)
            ? id => string.Equals(id, nameOrId, StringComparison.OrdinalIgnoreCase)
            : id => string.Equals(_ids.GetName(id), nameOrId, StringComparison.OrdinalIgnoreCase));

    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
    {
        ArgumentNullException.ThrowIfNull(migrationClass);

        var migration = (Migration)Activator.CreateInstance(migrationClass.AsType())!;
        migration.ActiveProvider = activeProvider;
        return migration;
    }

    private Contents Load()
    {
        List<TypeInfo> stream = [.. Assembly.DefinedTypes.Where(type =>
            !type.IsAbstract
            && !type.IsGenericTypeDefinition
            && (type.IsSubclassOf(typeof(Migration)) || type.IsSubclassOf(typeof(ModelSnapshot)))
            && type.GetCustomAttribute<DbContextAttribute>()?.ContextType == _contextType)];

        List<TypeInfo> own = [.. stream.Where(type => SetOf(type) == Set)];
        var perProvider = own.Count > 0 || stream.Any(type => PluginMigrationSets.Names.Contains(SetOf(type)));
        var set = perProvider ? own : stream;

        var migrations = new SortedList<string, TypeInfo>(StringComparer.Ordinal);
        foreach (var type in set.Where(type => type.IsSubclassOf(typeof(Migration))))
        {
            if (type.GetCustomAttribute<MigrationAttribute>()?.Id is { } id)
            {
                migrations.Add(id, type);
            }
        }

        var snapshot = set.FirstOrDefault(type => type.IsSubclassOf(typeof(ModelSnapshot))) is { } snapshotType
            ? (ModelSnapshot)Activator.CreateInstance(snapshotType.AsType())!
            : null;

        // A snapshot alone, as a half-scaffolded set leaves, applies nothing.
        return new(migrations, snapshot, perProvider, perProvider && migrations.Count > 0);
    }

    private static string? SetOf(TypeInfo type) => type.Namespace is { } name ? name[(name.LastIndexOf('.') + 1)..] : null;

    private sealed record Contents(IReadOnlyDictionary<string, TypeInfo> Migrations, ModelSnapshot? Snapshot, bool PerProvider, bool HasSet);
}
