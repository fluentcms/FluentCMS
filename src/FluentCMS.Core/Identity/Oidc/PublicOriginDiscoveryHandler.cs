using FluentCMS.Core.DependencyInjection;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace FluentCMS.Core.Identity.Oidc;

// OpenIddict builds the endpoint URLs of the discovery document from the request even when the issuer is set, so with a
// public origin a forged Host would still name its own host there. The request has been matched to the endpoint by now,
// so changing the base only changes the URLs the document names.
internal sealed class PublicOriginDiscoveryHandler(PublicOrigin origin) : IOpenIddictServerHandler<HandleConfigurationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<HandleConfigurationRequestContext>()
            .UseSingletonHandler<PublicOriginDiscoveryHandler>()
            .SetOrder(OpenIddictServerHandlers.Discovery.AttachEndpoints.Descriptor.Order - 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(HandleConfigurationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (origin.Base is { } value)
        {
            context.BaseUri = new Uri(value + "/", UriKind.Absolute);
        }

        return ValueTask.CompletedTask;
    }
}
