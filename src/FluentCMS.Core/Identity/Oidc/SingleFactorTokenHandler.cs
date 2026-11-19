using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace FluentCMS.Core.Identity.Oidc;

// Marks the entry of a token of a consent issued to an admin user without the second factor with the server session it was
// issued in. A code of a client with RequireConsent is issued under the user's consent, which belongs to no session, so a
// sign-in with the second factor finds that session's tokens by this mark rather than by their authorization (#3228).
internal sealed class SingleFactorTokenHandler(IOpenIddictTokenManager tokens) : IOpenIddictServerHandler<GenerateTokenContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<GenerateTokenContext>()
            .UseScopedHandler<SingleFactorTokenHandler>()
            .SetOrder(OpenIddictServerHandlers.Protection.CreateTokenEntry.Descriptor.Order + 100)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(GenerateTokenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var principal = context.Principal;
        if (principal is null
            || principal.GetClaim(CmsClaimTypes.Kind) != nameof(PrincipalKind.AdminUser)
            || principal.HasClaim(Claims.AuthenticationMethodReference, TokenClaims.MultiFactor)
            || principal.GetClaim(TokenClaims.ConsentSession) is not { Length: > 0 } serverSession
            || principal.GetTokenId() is not { } id
            || await tokens.FindByIdAsync(id, context.CancellationToken) is not { } token)
        {
            return;
        }

        var descriptor = new OpenIddictTokenDescriptor();
        await tokens.PopulateAsync(descriptor, token, context.CancellationToken);
        descriptor.Properties[UserSessions.ServerSessionProperty] = JsonSerializer.SerializeToElement(serverSession);
        await tokens.UpdateAsync(token, descriptor, context.CancellationToken);
    }
}
