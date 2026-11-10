namespace FluentCMS.Agents;

/// <summary>
/// The tool, resource and prompt providers that the plugins of the host added, in plugin order. Core implements it and
/// registers each provider as a scoped service of its own type; the MCP plugin reads it to serve them, and skips the ones
/// of a plugin that <see cref="Plugins.IPluginState"/> says is disabled for the site of a call.
/// </summary>
public interface IMcpProviders
{
    /// <summary>The implementations of <see cref="IMcpToolProvider"/>.</summary>
    IReadOnlyList<McpProviderRegistration> Tools { get; }

    /// <summary>The implementations of <see cref="IMcpResourceProvider"/>.</summary>
    IReadOnlyList<McpProviderRegistration> Resources { get; }

    /// <summary>The implementations of <see cref="IMcpPromptProvider"/>.</summary>
    IReadOnlyList<McpProviderRegistration> Prompts { get; }
}

/// <summary>A provider a plugin added.</summary>
/// <param name="Plugin">The name of the plugin that added it.</param>
/// <param name="Provider">The type of the provider, which the scope of a request resolves.</param>
public sealed record McpProviderRegistration(string Plugin, Type Provider);
