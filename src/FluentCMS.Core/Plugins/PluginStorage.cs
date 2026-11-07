using System.Reflection;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    public string TablePrefix => StorageNames.Prefix + plugin.Name.Replace('-', '_') + "_";

    public string HistoryTable => StorageNames.Prefix + plugin.Name.Replace('-', '_') + StorageNames.HistoryTableSuffix;

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
        }

        _contributors.Add(contributor);
        return names;
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
        var key = $"{provider.Conventions.CacheKey}|{contributors.Key}";
        var assembly = Migrations ?? plugin.Type.Assembly;
        var history = HistoryTable;

        services.AddDbContext<PluginDbContext<TPlugin>>(options =>
        {
            provider.ConfigurePluginMigrations(options, assembly, history);
            PluginModelOptionsExtension.Use(options, contributors.Contributors, provider.Conventions.ConfigureConventions, key);
            options.ReplaceService<IModelCacheKeyFactory, CmsModelCacheKeyFactory>();
        });

        if (Migrations is not null)
        {
            services.AddSingleton<IPluginMigrations>(new Stream<TPlugin>(plugin.Name));
        }
    }

    private sealed class Stream<TPlugin>(string name) : IPluginMigrations
        where TPlugin : class, IPlugin
    {
        public string Name => name;

        public DbContext ResolveContext(IServiceProvider scopedServices) => scopedServices.GetRequiredService<PluginDbContext<TPlugin>>();
    }
}
