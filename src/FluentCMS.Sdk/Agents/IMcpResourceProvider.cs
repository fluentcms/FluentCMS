namespace FluentCMS.Agents;

/// <summary>
/// Offers resources to the MCP server. A plugin adds one with
/// <see cref="Plugins.IPluginServiceBuilder.AddMcpResources{TProvider}"/>. Every call runs inside the operation of its
/// request, as the caller, and the providers of a plugin that is disabled for the site of the call are skipped.
/// </summary>
public interface IMcpResourceProvider
{
    /// <summary>Lists the resources and resource templates the principal of the operation may read.</summary>
    /// <param name="request">The operation the list is made for.</param>
    /// <param name="cancellationToken">Cancels the list.</param>
    /// <returns>The resources. A URI that a provider of an earlier plugin has already listed is left out by the server.</returns>
    ValueTask<IReadOnlyList<McpResourceDescriptor>> ListResourcesAsync(McpListRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads a resource.</summary>
    /// <param name="request">The URI and the operation of the read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The contents, or <see langword="null"/> when the URI is not one this provider serves to the principal, so that the
    /// server asks the next provider.
    /// </returns>
    ValueTask<IReadOnlyList<McpResourceContent>?> ReadResourceAsync(McpResourceRead request, CancellationToken cancellationToken = default);
}
