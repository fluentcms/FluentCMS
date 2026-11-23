using System.Transactions;
using FluentCMS.Sites;
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

    /// <summary>
    /// Holds the row of a site shared to the end of the context's current transaction, as a content write that adds an entry
    /// does, so that the site's deletion waits for the transaction and a row it adds goes with the plugin's cleanup of
    /// <c>SiteDeletedEvent</c>. Call it in the transaction, before the rows of the site, and hold several sites in ascending
    /// order of <see cref="SiteId.Value"/>.
    /// </summary>
    /// <param name="site">The site the transaction writes rows of.</param>
    /// <param name="cancellationToken">Cancels the wait for the hold.</param>
    /// <returns>A task that completes once the site is held.</returns>
    /// <exception cref="FluentCMS.Content.ValidationException">The site does not exist, or was deleted (<c>FCMS2301</c>).</exception>
    /// <exception cref="InvalidOperationException">The context has no transaction open, or core did not configure it.</exception>
    public Task HoldSiteAsync(SiteId site, CancellationToken cancellationToken = default)
    {
        // A hold outside a transaction would end with its statement and guard nothing.
        if (Database.CurrentTransaction is null && Database.GetEnlistedTransaction() is null)
        {
            throw new InvalidOperationException("A site is held to the end of a transaction: begin one on the context (Database.BeginTransactionAsync) before HoldSiteAsync, and write the rows of the site in it.");
        }

        var hold = _model?.SiteHold ?? throw new InvalidOperationException("The context was not configured by FluentCMS, so it cannot hold a site.");
        return hold.HoldAsync(this, site.Value, cancellationToken);
    }

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
