using FluentCMS.Agents;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace FluentCMS.Core.Identity.Oidc;

// Names /connect/register in the discovery document as registration_endpoint (RFC 8414), which is where an MCP client
// looks for it. Only while a client can register there: a host without the MCP plugin has no policy, and a policy that is
// Off answers 404, so neither names an endpoint a client would be refused at.
internal sealed class RegistrationDiscoveryHandler(IServiceProvider services, PublicOrigin origin) : IOpenIddictServerHandler<HandleConfigurationRequestContext>
{
    public const string Metadata = "registration_endpoint";

    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<HandleConfigurationRequestContext>()
            .UseScopedHandler<RegistrationDiscoveryHandler>()
            .SetOrder(OpenIddictServerHandlers.Discovery.AttachEndpoints.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(HandleConfigurationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (services.GetService<IMcpClientRegistrationPolicy>() is not { } policy
            || (await policy.GetAsync(context.CancellationToken)).Mode == McpClientRegistrationMode.Off)
        {
            return;
        }

        var http = context.Transaction.GetHttpRequest() ?? throw new InvalidOperationException("The authorization server handles a request outside ASP.NET Core.");
        context.Metadata[Metadata] = origin.Link(http, DynamicClientRegistration.Path).AbsoluteUri;
    }
}
