using FluentCMS.Content;

namespace FluentCMS.Core.Content;

// Whether the reads of a scope answer in the Management shape of spec 4.4, which carries the pending schedules of each
// entry's locale. The Management API's content routes ask for it for their request, and an operation that entered through
// MCP has it, since the MCP tools answer as those routes do. Delivery, GraphQL, jobs and every other read answer without
// it. The schedules still need a principal that manages, so a Delivery key reading through MCP does not see them.
internal sealed class ManagementShape(IOperationContextAccessor operations)
{
    public bool Asked { get; set; }

    public bool Applies => Asked || operations.Current?.Origin == Origin.Mcp;
}
