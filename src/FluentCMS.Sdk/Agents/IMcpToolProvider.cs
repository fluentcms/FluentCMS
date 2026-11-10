namespace FluentCMS.Agents;

/// <summary>
/// Offers tools to the MCP server, which the MCP plugin runs at <c>/mcp</c>. A plugin adds one with
/// <see cref="Plugins.IPluginServiceBuilder.AddMcpTools{TProvider}"/>. Every call runs inside the operation of its request,
/// as the caller, so the provider reaches content through the services, which enforce the caller's permissions, and lists
/// only what the caller may use. The providers of a plugin that is disabled for the site of the call are skipped.
/// </summary>
public interface IMcpToolProvider
{
    /// <summary>Lists the tools the principal of the operation may use.</summary>
    /// <param name="request">The operation the list is made for.</param>
    /// <param name="cancellationToken">Cancels the list.</param>
    /// <returns>The tools. A name that a provider of an earlier plugin has already listed is left out by the server.</returns>
    ValueTask<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(McpListRequest request, CancellationToken cancellationToken = default);

    /// <summary>Calls a tool.</summary>
    /// <param name="request">The tool, its arguments and the operation of the call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The result, or <see langword="null"/> when this provider offers no tool of that name to the principal, so that the
    /// server asks the next provider. A failure the caller can act on is a result made with
    /// <see cref="McpToolResult.Error"/>; an exception of the engine, such as a refused permission or a validation
    /// failure, may be left to the server, which answers it with the code the REST APIs give it.
    /// </returns>
    ValueTask<McpToolResult?> CallToolAsync(McpToolCall request, CancellationToken cancellationToken = default);
}
