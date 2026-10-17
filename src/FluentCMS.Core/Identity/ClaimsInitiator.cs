using System.Security.Claims;

namespace FluentCMS.Core.Identity;

public static class ClaimsInitiator
{
    public static Initiator? From(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var displayName = Value(principal, FluentCmsClaimTypes.DisplayName)
            ?? principal.Identity.Name
            ?? string.Empty;

        // The agent claim wins over any user id claim, so an agent can never be recorded as a person (SEC-01, AUD-01).
        if (Guid.TryParse(Value(principal, FluentCmsClaimTypes.AgentCredentialId), out var credentialId))
        {
            return new AgentInitiator(credentialId, displayName);
        }

        if (Guid.TryParse(Value(principal, ClaimTypes.NameIdentifier), out var userId))
        {
            return new UserInitiator(userId, displayName);
        }

        return null;
    }

    private static string? Value(ClaimsPrincipal principal, string claimType)
    {
        return principal.FindFirst(claimType)?.Value;
    }
}
