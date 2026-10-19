namespace FluentCMS.Core.Agents;

public static class AgentPermissionErrorCodes
{
    public const string TransitionNotPermitted = "agent_transition_not_permitted";
    public const string CapExceeded = "agent_cap_exceeded";
    public const string SiteOutOfScope = "agent_site_out_of_scope";
    public const string NotOwner = "agent_not_change_set_owner";
    public const string ScopeDenied = "agent_scope_denied";
    public const string SystemNotPermitted = "system_not_permitted";
}
