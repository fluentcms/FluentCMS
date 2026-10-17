using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Mcp;

public static class McpServiceCollectionExtensions
{
    // The MCP transport and tools arrive with F-MCP; until then this registers nothing.
    public static IServiceCollection AddFluentCmsMcp(this IServiceCollection services)
    {
        return services;
    }
}
