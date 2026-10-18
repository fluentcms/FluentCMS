using System.Security.Claims;
using FluentCMS.Core.Identity;

namespace FluentCMS.Core.Agents;

// The principal a call runs under once its key is accepted, on either transport. It carries the claims ClaimsInitiator reads,
// so every service records the call as this agent and never as a person (SEC-01, AUD-01).
public static class AgentPrincipal
{
    public static ClaimsPrincipal For(AgentCredential credential, string authenticationType)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);

        var identity = new ClaimsIdentity(
            [
                new Claim(FluentCmsClaimTypes.AgentCredentialId, credential.Id.ToString()),
                new Claim(FluentCmsClaimTypes.DisplayName, credential.DisplayName),
                new Claim(ClaimTypes.Name, credential.DisplayName),
            ],
            authenticationType);

        return new ClaimsPrincipal(identity);
    }
}
