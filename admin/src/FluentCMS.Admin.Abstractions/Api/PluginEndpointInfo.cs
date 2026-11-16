namespace FluentCMS.Admin.Api;

/// <summary>An endpoint a plugin maps.</summary>
public sealed record PluginEndpointInfo
{
    /// <summary>The HTTP method, or <c>*</c>.</summary>
    public required string Method { get; init; }

    /// <summary>The route template.</summary>
    public required string Route { get; init; }
}
