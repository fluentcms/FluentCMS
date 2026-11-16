namespace FluentCMS.Admin.Api;

/// <summary>Whether a plugin is enabled in one site.</summary>
public sealed record PluginSiteInfo
{
    /// <summary>The id of the site.</summary>
    public required Guid SiteId { get; init; }

    /// <summary>The slug of the site.</summary>
    public required string Slug { get; init; }

    /// <summary>Whether the plugin is enabled there.</summary>
    public required bool Enabled { get; init; }
}
