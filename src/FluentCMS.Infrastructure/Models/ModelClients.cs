using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Anthropic;
using FluentCMS.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace FluentCMS.Infrastructure.Models;

// The only code that references a provider client (ADR-0008, MDL-01). Each profile gets one client, kept for its later calls, whose
// HTTP requests pass the outbound allow-list (SEC-05).
internal sealed class ModelClients(IOptions<ModelOptions> options, IOutboundHosts outbound, ModelTransport transport) : IModelClients, IDisposable
{
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.Ordinal);

    public IChatClient Create(ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return _clients.GetOrAdd(profile.Name, _ => Build(profile, options.Value.Profiles[profile.Name]));
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
    }

    private IChatClient Build(ModelProfile profile, ModelProfileOptions settings)
    {
        var http = new HttpClient(new OutboundAllowListHandler(outbound) { InnerHandler = transport.CreateHandler() }) { Timeout = settings.Timeout };

        return profile.Provider switch
        {
            ModelProviders.OpenAi => new OpenAIClient(
                    new ApiKeyCredential(settings.ApiKey is { Length: > 0 } key ? key : "none"),
                    new OpenAIClientOptions { Endpoint = profile.Endpoint, Transport = new HttpClientPipelineTransport(http) })
                .GetChatClient(profile.Model)
                .AsIChatClient(),
            ModelProviders.Anthropic => new AnthropicClient
                {
                    ApiKey = settings.ApiKey,
                    BaseUrl = profile.Endpoint.ToString(),
                    HttpClient = http,
                }
                .AsIChatClient(profile.Model, profile.MaxOutputTokens),
            _ => throw new InvalidOperationException($"The provider '{profile.Provider}' of the '{profile.Name}' model has no wiring."),
        };
    }
}

// The innermost HTTP handler of provider clients. It does not follow redirects, so each hop passes the allow-list; tests swap it
// for an in-process fake of the provider's API.
public sealed class ModelTransport(Func<HttpMessageHandler> createHandler)
{
    public static ModelTransport Network { get; } = new(() => new SocketsHttpHandler { AllowAutoRedirect = false });

    public HttpMessageHandler CreateHandler()
    {
        return createHandler();
    }
}
