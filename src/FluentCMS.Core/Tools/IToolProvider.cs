using FluentCMS.Core.Extensions;

namespace FluentCMS.Core.Tools;

// The tools of one capability, which agents call over MCP (MCP-01, MCP-02, EXT-01). Built-ins under FluentCMS.Core.Builtin and
// plugins implement it alike, and every implementation must pass ToolProviderConformanceTests in tests/FluentCMS.Tests/Tools.
[ExtensionPoint]
public interface IToolProvider
{
    IReadOnlyList<AgentTool> Tools { get; }
}
