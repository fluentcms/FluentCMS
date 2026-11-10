using FluentCMS.Core.Api.Common;
using Microsoft.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace FluentCMS.Core.Identity.Oidc;

// The resource parameter of RFC 8707, which an MCP client sends with its authorization and token requests. It is taken
// when it is the public URL of a protected resource of this host and refused with invalid_target otherwise. OpenIddict
// checks it against a list fixed in its options, which cannot hold a URL that follows the request when no public origin
// is set, so that check is off and this one stands in for it, where that one runs. The parameter changes nothing in the
// token: the audience stays the instance's, and the scope of the resource is what holds a token to it.
internal sealed class ResourceIndicatorHandler(ProtectedResources resources)
    : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>, IOpenIddictServerHandler<ValidateTokenRequestContext>
{
    private const string Refusal = "The resource parameter names no protected resource of this server.";

    public static OpenIddictServerHandlerDescriptor AuthorizationDescriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
            .UseSingletonHandler<ResourceIndicatorHandler>()
            .SetOrder(OpenIddictServerHandlers.Authentication.ValidateResources.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public static OpenIddictServerHandlerDescriptor TokenDescriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateTokenRequestContext>()
            .UseSingletonHandler<ResourceIndicatorHandler>()
            .SetOrder(OpenIddictServerHandlers.Exchange.ValidateResources.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Accepts(context.Transaction, context.Request))
        {
            context.Reject(Errors.InvalidTarget, Refusal);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(ValidateTokenRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Accepts(context.Transaction, context.Request))
        {
            context.Reject(Errors.InvalidTarget, Refusal);
        }

        return ValueTask.CompletedTask;
    }

    private bool Accepts(OpenIddictServerTransaction transaction, OpenIddictRequest request)
    {
        var named = request.GetResources();
        if (named.IsDefaultOrEmpty)
        {
            return true;
        }

        var http = transaction.GetHttpRequest() ?? throw new InvalidOperationException("The authorization server handles a request outside ASP.NET Core.");
        return named.All(resource => resources.Names(http, resource));
    }
}
