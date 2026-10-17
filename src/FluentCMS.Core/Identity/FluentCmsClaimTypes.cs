namespace FluentCMS.Core.Identity;

public static class FluentCmsClaimTypes
{
    public const string DisplayName = "fluentcms:display_name";

    // Present only on principals authenticated with an agent credential (F-MCP).
    public const string AgentCredentialId = "fluentcms:agent_credential_id";
}
