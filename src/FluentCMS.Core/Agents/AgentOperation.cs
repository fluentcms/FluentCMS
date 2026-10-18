namespace FluentCMS.Core.Agents;

// What a credential may do to the items in its scope. Approve and publish are absent on purpose: no scope grants them (MCP-06, SEC-02).
public enum AgentOperation
{
    Read,
    Create,
    Update,
    Delete,
}
