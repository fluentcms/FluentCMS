using System.Security.Claims;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;

namespace FluentCMS.Tests.Agents;

public sealed class AgentPrincipalTests
{
    [Fact]
    public void The_principal_of_a_credential_is_recorded_as_that_agent()
    {
        var credential = TestAgentCredentials.Issue();

        var principal = AgentPrincipal.For(credential, "AgentKey");

        Assert.True(principal.Identity!.IsAuthenticated);
        Assert.Equal("AgentKey", principal.Identity.AuthenticationType);
        Assert.Equal("catalog-sync", principal.Identity.Name);
        Assert.Equal(credential.Initiator, ClaimsInitiator.From(principal));
    }

    [Fact]
    public void It_carries_no_user_id_so_it_can_never_be_recorded_as_a_person()
    {
        var principal = AgentPrincipal.For(TestAgentCredentials.Issue(), "AgentKey");

        Assert.Null(principal.FindFirst(ClaimTypes.NameIdentifier));
        Assert.IsType<AgentInitiator>(ClaimsInitiator.From(principal));
    }
}
