using FluentCMS.Content;

namespace FluentCMS.Plugins;

/// <summary>What <see cref="PluginAppBuilderExtensions.MapProtectedResource"/> needs to know of a protected resource.</summary>
public sealed class ProtectedResourceOptions
{
    /// <summary>
    /// The scope a bearer token needs to use the resource, one the authorization server offers, such as <c>mcp</c>. The
    /// metadata of the resource lists it.
    /// </summary>
    public required string Scope { get; init; }

    /// <summary>
    /// Where the operations of the group enter the system: <see cref="Origin.Api"/> or <see cref="Origin.Mcp"/>. It is
    /// recorded on every version and activity row they write.
    /// </summary>
    public Origin Origin { get; init; } = Origin.Api;

    /// <summary>A name of the resource for people, which the metadata carries, or <see langword="null"/>.</summary>
    public string? Name { get; init; }
}
