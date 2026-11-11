using FluentCMS.Sites;
using Microsoft.Extensions.AI;

namespace FluentCMS.Plugins.AiAssist;

// The provider slot of the plugin: the chat client and the model of a site. The default builds them from the site's "ai"
// settings; a host that fills the slot with its own brings any other IChatClient, such as one that signs in to Azure with
// an identity. The interface is here and not in the SDK, which takes no Microsoft.Extensions.AI dependency.
public interface IAiClientFactory
{
    // Null when the site has no provider, which leaves AI assist off there. The factory owns the client: the plugin calls
    // it and does not dispose it. A site whose settings cannot be used is refused with a PluginException.
    ValueTask<AiClient?> CreateAsync(SiteId site, AiSettings settings, CancellationToken cancellationToken = default);
}

// Provider and Model are what the usage log and the answers name.
public sealed record AiClient(IChatClient Client, string Provider, string Model);
