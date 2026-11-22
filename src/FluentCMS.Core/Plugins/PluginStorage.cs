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

    // Mapped on a model without conventions, which is enough to read the names of the tables and views. A name can still be
    // another's, since one plugin's prefix can hold another's (cms_ai_ holds cms_ai_assist_) or a system table's, and
    // takenBy names its owner. Returns the names.
    public IReadOnlyList<string> AddModel(IPluginModelContributor contributor, Func<string, string?> takenBy)
    {
        ArgumentNullException.ThrowIfNull(contributor);

        var model = new ModelBuilder();
        contributor.Configure(model);
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
    // runs on SQLite does not fail on PostgreSQL later (#3533). Only the names the model gives are held to it: EF cuts the
    // ones it makes to the provider's limit.
    private void CheckLengths(IPluginModelContributor contributor, IMutableEntityType entity)
    {
        foreach (var (what, name) in GivenNames(entity))
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

    private static IEnumerable<(string What, string Name)> GivenNames(IMutableEntityType entity)
    {
        foreach (var (what, name) in new[] { ("the table", entity.GetTableName()), ("the view", entity.GetViewName()) })
        {
            if (name is not null)
            {
                yield return (what, name);
            }
        }

        foreach (var property in Properties(entity))
        {
            if (property[RelationalAnnotationNames.ColumnName] is string column)
            {
                yield return ("the column", column);
            }
        }

        foreach (var key in entity.GetDeclaredKeys())
        {
            if (key[RelationalAnnotationNames.Name] is string name)
            {
                yield return ("the key", name);
            }
        }

        foreach (var foreignKey in entity.GetDeclaredForeignKeys())
        {
            if (foreignKey[RelationalAnnotationNames.Name] is string name)
            {
                yield return ("the foreign key", name);
            }
        }

        foreach (var index in entity.GetDeclaredIndexes())
        {
            if ((index[RelationalAnnotationNames.Name] as string ?? index.Name) is { } name)
            {
                yield return ("the index", name);
            }
        }

        // A check constraint's name is always the model's.
        foreach (var check in entity.GetDeclaredCheckConstraints())
        {
            if (check.Name is { } name)
            {
                yield return ("the check constraint", name);
            }
        }

        static IEnumerable<IMutableProperty> Properties(IMutableTypeBase type) =>
            type.GetDeclaredProperties().Concat(type.GetDeclaredComplexProperties().SelectMany(complex => Properties(complex.ComplexType)));
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
            options.AddInterceptors(container.GetRequiredService<PluginTransactions>());
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
