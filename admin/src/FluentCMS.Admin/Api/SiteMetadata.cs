namespace FluentCMS.Admin.Api;

// /schema and /plugins, read once per site per session and again after a site switch or a reload; the schema changes only
// with a new server process (spec 2.7). The schema is read in the content locale, since the server narrows it to the types
// and fields the caller may read there (#3178), and again when the locale changes; the plugins are the site's. Nothing else
// is cached. Callers share one read, and a read that failed is not kept.
internal sealed class SiteMetadata(ManagementClient client, ICurrentSite current, IContentLocale contentLocale) : IAdminSchema
{
    private readonly Lock _gate = new();
    private string? _site;
    private string? _locale;
    private Task<SchemaDescription>? _schema;
    private Task<IReadOnlyList<PluginInfo>>? _plugins;

    public Task<SchemaDescription> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        Task<SchemaDescription> read;
        lock (_gate)
        {
            var site = SwitchTo(current.Site);
            var locale = contentLocale.Locale;
            if (!string.Equals(locale, _locale, StringComparison.Ordinal))
            {
                (_locale, _schema) = (locale, null);
            }

            read = Usable(_schema) ?? (_schema = client.GetSchemaAsync(site, locale, CancellationToken.None));
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

    // A read in the site's default locale names no locale, so a new default locale of the site needs the schema read anew.
    public void ForgetSchema()
    {
        lock (_gate)
        {
            _schema = null;
        }
    }

    // A read the shell gave up on, so that the next caller sends a new one.
    public void ForgetPending()
    {
        lock (_gate)
        {
            _schema = _schema is { IsCompleted: false } ? null : _schema;
            _plugins = _plugins is { IsCompleted: false } ? null : _plugins;
        }
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
