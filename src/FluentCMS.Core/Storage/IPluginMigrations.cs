using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Storage;

// The seam for the plugin stream. A plugin's context has the history table cms_{plugin}__migrations. The runner
// applies the streams in the order they are registered; the plugin host of M3 registers them in dependency order.
public interface IPluginMigrations
{
    string Name { get; }

    // From the scope the runner passes, which disposes it.
    DbContext ResolveContext(IServiceProvider scopedServices);
}
