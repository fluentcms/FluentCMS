namespace FluentCMS.Admin.Api;

/// <summary>A plugin of the instance, from <c>GET /plugins</c>, which gates the contributions of its admin half.</summary>
public sealed record PluginInfo
{
    /// <summary>The name of the plugin, which its admin half's manifest names.</summary>
    public required string Name { get; init; }

    /// <summary>The version of the plugin.</summary>
    public required string Version { get; init; }

    /// <summary>The range of the SDK the plugin was built for.</summary>
    public required string SdkRange { get; init; }

    /// <summary>The plugins it depends on.</summary>
    public required IReadOnlyList<string> DependsOn { get; init; }

    /// <summary>Whether the plugin is enabled in the site of the request.</summary>
    public required bool Enabled { get; init; }

    /// <summary>What the plugin adds to the instance.</summary>
    public required PluginContributionsInfo Contributions { get; init; }

    /// <summary>The plugin's enablement in each site the caller may see.</summary>
    public required IReadOnlyList<PluginSiteInfo> Sites { get; init; }
}
