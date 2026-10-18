using FluentCMS.Core.Extensions;

namespace FluentCMS.Core.Tools;

// What agents read by URI over MCP: the schema, and later the active rules and an entry or entity (MCP-03, EXT-01). Every
// implementation must pass ToolProviderConformanceTests in tests/FluentCMS.Tests/Tools.
[ExtensionPoint]
public interface IResourceProvider
{
    IReadOnlyList<AgentResource> Resources { get; }
}
