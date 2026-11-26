namespace FluentCMS.Core.Storage.Entities;

// A row of cms_plugin_store: one value of IPluginStore<TPlugin>, which a plugin writes only through it. A plugin that is
// no longer registered keeps its rows, which say nothing then.
public sealed class PluginStoreRow
{
    public const int KeyMaxLength = 128;
    public const int ValueMaxLength = 4000;

    public string Plugin { get; set; } = "";

    public string Key { get; set; } = "";

    public string Value { get; set; } = "";

    public long Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
