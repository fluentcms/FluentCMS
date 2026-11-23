using System.Reflection;
using System.Text;
using FluentCMS.Core.Caching;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Plugins;

// The plugins' model contributors in plugin order, which CmsDbContext maps after the system tables. Key tells the union
// models of two hosts apart in the model cache.
internal sealed record PluginModels(IReadOnlyList<IPluginModelContributor> Contributors, string Key)
{
    public static PluginModels None { get; } = new([], string.Empty);

    public static PluginModels Of(IEnumerable<IPluginModelContributor> contributors)
    {
        List<IPluginModelContributor> list = [.. contributors];
        return list.Count == 0 ? None : new(list, string.Join(';', list.Select(contributor => contributor.GetType().AssemblyQualifiedName)));
    }
}

// The tables of one plugin: its contributors, whose tables are named cms_{plugin}_..., and the assembly of its migration
// stream, whose history table is cms_{plugin}__migrations.
internal sealed class PluginStorage(RegisteredPlugin plugin)
{
    private static readonly MethodInfo RegisterMethod = typeof(PluginStorage).GetMethod(nameof(Register), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<IPluginModelContributor> _contributors = [];

    public RegisteredPlugin Plugin => plugin;

    public IReadOnlyList<IPluginModelContributor> Contributors => _contributors;

    public Assembly? Migrations { get; private set; }

    public string TablePrefix => TablePrefixOf(plugin.Name);

    public string HistoryTable => HistoryTableOf(plugin.Name);

    // Mapped on a model without conventions, which is enough to read the names of the tables and views, and of what the
    // contributor configures on them; what only the conventions would find, such as a property no call names, is not there
    // to read. A name can still be
    // another's, since one plugin's prefix can hold another's (cms_ai_ holds cms_ai_assist_) or a system table's, and
    // takenBy names its owner. Returns the names.
    public IReadOnlyList<string> AddModel(IPluginModelContributor contributor, Func<string, string?> takenBy)
    {
        ArgumentNullException.ThrowIfNull(contributor);

        var model = new ModelBuilder();
        contributor.Configure(model);
        model.Model.SetMaxIdentifierLength(StorageNames.MaxIdentifierLength);
        List<string> names = [];
        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var name in new[] { entity.GetTableName(), entity.GetViewName() }.OfType<string>())
            {
                var problem = !name.StartsWith(TablePrefix, StringComparison.Ordinal) ? $"the tables of a plugin are named {TablePrefix}..."
                    : name == HistoryTable ? $"{HistoryTable} is its migration history"
                    : takenBy(name) is { } owner ? $"which is a table of {owner}"
                    : null;
                if (problem is not null)
                {
                    throw new FluentCmsConfigurationException(
                        PluginErrorCodes.TableOutsidePrefix,
                        $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) maps {entity.DisplayName()} to '{name}' in {contributor.GetType().FullName}; {problem}.");
                }

                names.Add(name);
            }

            CheckLengths(contributor, entity);
        }

        _contributors.Add(contributor);
        return names;
    }

    // PostgreSQL keeps the first 63 bytes of a name and drops the rest without a word, so the baseline check, which finds the
    // tables, columns and indexes of the model by name, would not find them. Refused on every provider, so that a plugin that
    // runs on SQLite does not fail on PostgreSQL later (#3533). The names are read as EF gives them on PostgreSQL: the model
    // has its limit of 63, so a name EF derives, such as IX_..., is cut as EF cuts it there, by characters, and one with
    // characters of more than a byte can still be too long.
    private void CheckLengths(IPluginModelContributor contributor, IMutableEntityType entity)
    {
        foreach (var (what, name) in NamesOnPostgreSql(entity))
        {
            var bytes = Encoding.UTF8.GetByteCount(name);
            if (bytes > StorageNames.MaxIdentifierLength)
            {
                throw new FluentCmsConfigurationException(
                    PluginErrorCodes.NameTooLong,
                    $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) gives {what} of {entity.DisplayName()} the name '{name}' in {contributor.GetType().FullName}, which has {bytes} bytes in UTF-8; a name has at most {StorageNames.MaxIdentifierLength}, as PostgreSQL keeps no more of it.");
            }
        }
    }

    // Each table or view the entity maps to, the fragments of entity splitting and the tables of TPT and TPC included, with
    // the columns, keys and indexes it has there, so that a column named for one table alone is read too.
    private static IEnumerable<(string What, string Name)> NamesOnPostgreSql(IMutableEntityType entity)
    {
        List<StoreObjectIdentifier> stores = [];
        foreach (var type in new[] { StoreObjectType.Table, StoreObjectType.View })
        {
            if (StoreObjectIdentifier.Create(entity, type) is { } store)
            {
                stores.Add(store);
            }
        }

        stores.AddRange(entity.GetMappingFragments().Select(fragment => fragment.StoreObject));
        foreach (var store in stores.Distinct())
        {
            yield return (store.StoreObjectType == StoreObjectType.View ? "the view" : "the table", store.Name);

            foreach (var column in Properties(entity).Select(property => property.GetColumnName(store)).OfType<string>())
            {
                yield return ("the column", column);
            }

            if (store.StoreObjectType != StoreObjectType.Table)
            {
                continue;
            }

            foreach (var key in entity.GetKeys().Select(key => key.GetName(store)).OfType<string>())
            {
                yield return ("the key", key);
            }

            foreach (var index in entity.GetIndexes().Select(index => index.GetDatabaseName(store)).OfType<string>())
            {
                yield return ("the index", index);
            }
        }

        foreach (var foreignKey in entity.GetDeclaredForeignKeys().Select(foreignKey => foreignKey.GetConstraintName()).OfType<string>())
        {
            yield return ("the foreign key", foreignKey);
        }

        // The name given, or else the one EF derives from the model's name.
        foreach (var check in entity.GetDeclaredCheckConstraints().Select(check => check.Name).OfType<string>())
        {
            yield return ("the check constraint", check);
        }

        static IEnumerable<IReadOnlyProperty> Properties(IReadOnlyTypeBase type) =>
            type.GetProperties().Concat(type.GetComplexProperties().SelectMany(complex => Properties(complex.ComplexType)));
    }

    public void AddMigrations(Assembly assembly)
    {
        if (Migrations is not null && Migrations != assembly)
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.SecondMigrations,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) adds migrations from {Migrations.GetName().Name} and from {assembly.GetName().Name}; a plugin has one migration stream, in one assembly.");
        }

        Migrations = assembly;
    }

    // The plugin's context, and its stream when it added migrations, which registered in plugin order run in that order.
    public void Register(IServiceCollection services, IDataProvider provider) =>
        RegisterMethod.MakeGenericMethod(plugin.Type).Invoke(this, [services, provider]);

    private void Register<TPlugin>(IServiceCollection services, IDataProvider provider)
        where TPlugin : class, IPlugin
    {
        var contributors = PluginModels.Of(_contributors);
        var assembly = Migrations ?? plugin.Type.Assembly;
        var history = HistoryTable;

        services.AddDbContext<PluginDbContext<TPlugin>>((container, options) =>
        {
            Configure(options, provider, contributors, assembly, history);
            options.AddInterceptors(container.GetRequiredService<PluginTransactions>(), container.GetRequiredService<PluginSiteHold>());
            LeaseRenewalInterceptor.AddTo(options, container, provider);
        });

        if (Migrations is not null)
        {
            services.AddSingleton<IPluginMigrations>(new Stream<TPlugin>(plugin.Name));
        }
    }

    public static string TablePrefixOf(string plugin) => StorageNames.Prefix + plugin.Replace('-', '_') + "_";

    public static string HistoryTableOf(string plugin) => StorageNames.Prefix + plugin.Replace('-', '_') + StorageNames.HistoryTableSuffix;

    // The options of a plugin's context, at run time and at design time alike.
    public static void Configure(DbContextOptionsBuilder options, IDataProvider provider, PluginModels contributors, Assembly migrations, string history)
    {
        provider.ConfigurePluginMigrations(options, migrations, history);
        PluginModelOptionsExtension.Use(options, contributors.Contributors, builder => StorageConventions.Configure(provider.Conventions, builder), $"{provider.Conventions.CacheKey}|{contributors.Key}");
        options.ReplaceService<IModelCacheKeyFactory, CmsModelCacheKeyFactory>();
    }

    private sealed class Stream<TPlugin>(string name) : IPluginMigrations
        where TPlugin : class, IPlugin
    {
        public string Name => name;

        public DbContext ResolveContext(IServiceProvider scopedServices) => scopedServices.GetRequiredService<PluginDbContext<TPlugin>>();
    }
}
