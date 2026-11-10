using FluentCMS.Content;

namespace FluentCMS.Agents;

/// <summary>A request to list the tools, resources or prompts of a provider.</summary>
/// <param name="Operation">The operation of the request: its principal, site, locale and client.</param>
public sealed record McpListRequest(OperationContext Operation);
