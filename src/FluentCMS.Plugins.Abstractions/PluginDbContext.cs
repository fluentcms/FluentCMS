using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins;

/// <summary>
/// The context of a plugin's tables: the one its migrations name in <c>[DbContext(typeof(PluginDbContext&lt;MyPlugin&gt;))]</c>,
/// with the history table <c>cms_{plugin}__migrations</c>, and the one its code queries them through. Core configures it with
/// the host's database and the plugin's model contributors; a plugin resolves it from the services of a scope. Core sees its
/// transactions: <c>IEventBus.PublishAsync</c> inside one that is open is refused, so a plugin publishes after it commits.
/// </summary>
/// <typeparam name="TPlugin">The plugin the context belongs to.</typeparam>
public sealed class PluginDbContext<TPlugin> : DbContext, IPluginModelKey
    where TPlugin : class, IPlugin
{
    private readonly PluginModelOptionsExtension? _model;

    /// <summary>Creates the context.</summary>
    /// <param name="options">The options core registers for it.</param>
    public PluginDbContext(DbContextOptions<PluginDbContext<TPlugin>> options)
        : base(options)
    {
        _model = options.FindExtension<PluginModelOptionsExtension>();
    }

    string IPluginModelKey.ModelKey => _model?.Key ?? string.Empty;

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        _model?.Conventions?.Invoke(configurationBuilder);

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var contributor in _model?.Contributors ?? [])
        {
            contributor.Configure(modelBuilder);
        }
    }
}
