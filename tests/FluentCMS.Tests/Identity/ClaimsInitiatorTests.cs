using System.Security.Claims;
using FluentCMS.Core.Identity;

namespace FluentCMS.Tests.Identity;

public sealed class ClaimsInitiatorTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CredentialId = Guid.NewGuid();

    [Fact]
    public void A_signed_in_user_is_a_user_initiator()
    {
        var principal = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(FluentCmsClaimTypes.DisplayName, "Amir"));

        var initiator = Assert.IsType<UserInitiator>(ClaimsInitiator.From(principal));

        Assert.Equal(UserId, initiator.UserId);
        Assert.Equal("Amir", initiator.DisplayName);
    }

    [Fact]
    public void An_agent_credential_claim_makes_an_agent_initiator_even_beside_a_user_id()
    {
        var principal = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(FluentCmsClaimTypes.AgentCredentialId, CredentialId.ToString()),
            new Claim(ClaimTypes.Name, "catalog-sync"));

        var initiator = Assert.IsType<AgentInitiator>(ClaimsInitiator.From(principal));

        Assert.Equal(CredentialId, initiator.CredentialId);
        Assert.Equal("catalog-sync", initiator.DisplayName);
    }

    [Fact]
    public void An_anonymous_or_unauthenticated_principal_has_no_initiator()
    {
        Assert.Null(ClaimsInitiator.From(null));
        Assert.Null(ClaimsInitiator.From(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(ClaimsInitiator.From(Authenticated(new Claim(ClaimTypes.Name, "no id claim"))));
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
