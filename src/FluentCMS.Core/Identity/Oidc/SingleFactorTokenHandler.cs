using System.Security.Claims;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace FluentCMS.Core.Identity.Oidc;

// Marks the entries of the tokens of a consent issued to an admin user without the second factor with the server session
// they were issued in. A code of a client with RequireConsent is issued under the user's consent, which belongs to no
// session, so a sign-in with the second factor finds that session's tokens by this mark rather than by their authorization
// (#3228). The session is read from the sign-in's principal, where it has no destination, so no token the client or a
// resource server reads carries it; codes and refresh tokens, which only the server reads, keep it for their redemption.
internal sealed class SingleFactorTokenHandler(IOpenIddictTokenManager tokens) : IOpenIddictServerHandler<ProcessSignInContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessSignInContext>()
            .UseScopedHandler<SingleFactorTokenHandler>()
            .SetOrder(OpenIddictServerHandlers.BeautifyGeneratedTokens.Descriptor.Order + 100)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(ProcessSignInContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var principal = context.Principal;
        if (principal is null
            || principal.GetClaim(CmsClaimTypes.Kind) != nameof(PrincipalKind.AdminUser)
            || principal.HasClaim(Claims.AuthenticationMethodReference, TokenClaims.MultiFactor)
            || principal.GetClaim(TokenClaims.ConsentSession) is not { Length: > 0 } serverSession)
        {
            return;
        }

        foreach (var issued in new ClaimsPrincipal?[] { context.AuthorizationCodePrincipal, context.AccessTokenPrincipal, context.RefreshTokenPrincipal })
        {
            if (issued?.GetTokenId() is not { } id || await tokens.FindByIdAsync(id, context.CancellationToken) is not { } token)
            {
                continue;
            }

            var descriptor = new OpenIddictTokenDescriptor();
            await tokens.PopulateAsync(descriptor, token, context.CancellationToken);
            descriptor.Properties[UserSessions.ServerSessionProperty] = JsonSerializer.SerializeToElement(serverSession);
            await tokens.UpdateAsync(token, descriptor, context.CancellationToken);
        }
    }
}
