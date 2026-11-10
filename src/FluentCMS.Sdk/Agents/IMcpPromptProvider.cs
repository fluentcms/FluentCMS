namespace FluentCMS.Agents;

/// <summary>
/// Offers prompts to the MCP server. A plugin adds one with
/// <see cref="Plugins.IPluginServiceBuilder.AddMcpPrompts{TProvider}"/>. Every call runs inside the operation of its
/// request, as the caller, and the providers of a plugin that is disabled for the site of the call are skipped.
/// </summary>
public interface IMcpPromptProvider
{
    /// <summary>Lists the prompts the principal of the operation may use.</summary>
    /// <param name="request">The operation the list is made for.</param>
    /// <param name="cancellationToken">Cancels the list.</param>
    /// <returns>The prompts. A name that a provider of an earlier plugin has already listed is left out by the server.</returns>
    ValueTask<IReadOnlyList<McpPromptDescriptor>> ListPromptsAsync(McpListRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets a prompt with its arguments filled in.</summary>
    /// <param name="request">The prompt, its arguments and the operation of the call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The messages, or <see langword="null"/> when this provider offers no prompt of that name to the principal, so that
    /// the server asks the next provider.
    /// </returns>
    ValueTask<McpPromptResult?> GetPromptAsync(McpPromptGet request, CancellationToken cancellationToken = default);
}
