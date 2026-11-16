namespace FluentCMS.Admin.Api;

/// <summary>What a plugin adds to the instance.</summary>
public sealed record PluginContributionsInfo
{
    /// <summary>The field types.</summary>
    public required IReadOnlyList<string> FieldTypes { get; init; }

    /// <summary>The permissions.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>The endpoints.</summary>
    public required IReadOnlyList<PluginEndpointInfo> Endpoints { get; init; }

    /// <summary>The job types.</summary>
    public required IReadOnlyList<string> Jobs { get; init; }

    /// <summary>The MCP tools.</summary>
    public required IReadOnlyList<string> Tools { get; init; }
}
