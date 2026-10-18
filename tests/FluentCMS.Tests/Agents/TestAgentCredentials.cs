using FluentCMS.Core.Agents;

namespace FluentCMS.Tests.Agents;

internal static class TestAgentCredentials
{
    public static readonly Guid Site = Guid.NewGuid();

    public static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    public static AgentScope Scope()
    {
        return AgentScope.Create([Site], ["product"], ["product.price"], [AgentOperation.Read, AgentOperation.Update]).Value;
    }

    public static AgentCaps Caps()
    {
        return AgentCaps.Create(600, TimeSpan.FromHours(1), 5.25m, TimeSpan.FromDays(1), 200, 20).Value;
    }

    public static NewAgentCredential New(string displayName = "catalog-sync", DateTimeOffset? expiresAt = null)
    {
        return new NewAgentCredential(displayName, expiresAt ?? Now.AddDays(90), Scope(), Caps());
    }

    public static AgentCredential Issue()
    {
        return AgentCredential.Issue(New(), Now).Value;
    }
}
