namespace FluentCMS.Admin.Api;

// /schema and /plugins, read once per site per session and again after a site switch or a reload; the schema changes only
// with a new server process (spec 2.7). Nothing else is cached. Callers share one read, and a read that failed is not kept.
internal sealed class SiteMetadata(ManagementClient client, ICurrentSite current)
{
    private readonly Lock _gate = new();
    private string? _site;
    private Task<SchemaDescription>? _schema;
    private Task<IReadOnlyList<PluginInfo>>? _plugins;

    public Task<SchemaDescription> GetSchemaAsync(CancellationToken cancellationToken)
    {
        Task<SchemaDescription> read;
        lock (_gate)
        {
            var site = SwitchTo(current.Site);
            read = Usable(_schema) ?? (_schema = client.GetSchemaAsync(site, CancellationToken.None));
        }

        return read.WaitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PluginInfo>> GetPluginsAsync(CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<PluginInfo>> read;
        lock (_gate)
        {
            var site = SwitchTo(current.Site);
            read = Usable(_plugins) ?? (_plugins = client.GetPluginsAsync(site, CancellationToken.None));
        }

        return read.WaitAsync(cancellationToken);
    }

    private string? SwitchTo(string? site)
    {
        if (!string.Equals(site, _site, StringComparison.Ordinal))
        {
            _site = site;
            _schema = null;
            _plugins = null;
        }

        return site;
    }

    private static Task<T>? Usable<T>(Task<T>? read) => read is { IsFaulted: false, IsCanceled: false } ? read : null;
}
