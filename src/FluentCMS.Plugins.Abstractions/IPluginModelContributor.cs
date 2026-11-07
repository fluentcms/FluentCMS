using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins;

/// <summary>
/// Maps the tables of a plugin, added with <see cref="PluginServiceBuilderExtensions.AddModel{TContributor}"/>. Every table
/// and view it maps is named <c>cms_{plugin}_...</c>, with the hyphens of the plugin's name as underscores, or the host does
/// not start. The same mapping builds the plugin's <see cref="PluginDbContext{TPlugin}"/> and joins the runtime model of
/// core, where it is left out of the other migration streams.
/// </summary>
public interface IPluginModelContributor
{
    /// <summary>Maps the plugin's entities.</summary>
    /// <param name="modelBuilder">The builder of the model, with the conventions of the data provider.</param>
    void Configure(ModelBuilder modelBuilder);
}
