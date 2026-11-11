using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;
using System.Text.Json;
using Anthropic;
using FluentCMS.Sites;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;

namespace FluentCMS.Plugins.AiAssist;

// The default of the IAiClientFactory slot: the client of the provider the site's settings name. OpenAI, Azure OpenAI and
// any OpenAI-compatible endpoint go through Microsoft.Extensions.AI.OpenAI, Anthropic through the IChatClient of its own
// package and Ollama through OllamaSharp. An endpoint that a site names is used only when it is one of
// FluentCms:Ai:AllowedEndpoints, and no redirect is followed, so that an allowed endpoint cannot lead the server elsewhere.
// A call is one request: the retries of the provider packages are off, so that a call is paid for and logged once.
internal sealed class AiClientFactory(IOptions<AiOptions> options, IConfiguration configuration, ILogger<AiClientFactory> logger) : IAiClientFactory, IDisposable
{
    public static readonly Uri OpenAiEndpoint = new("https://api.openai.com/v1");
    public static readonly Uri AnthropicEndpoint = new("https://api.anthropic.com");

    private readonly SocketsHttpHandler _handler = new() { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

    public static string NameOf(AiProvider provider) => JsonNamingPolicy.CamelCase.ConvertName(provider.ToString());

    public ValueTask<AiClient?> CreateAsync(SiteId site, AiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var provider = settings.Provider;
        if (provider == AiProvider.None)
        {
            return ValueTask.FromResult<AiClient?>(null);
        }

        if (!Enum.IsDefined(provider))
        {
            throw NotConfigured("name a provider the plugin has");
        }

        var model = settings.Model?.Trim();
        if (string.IsNullOrEmpty(model))
        {
            throw NotConfigured("name a model");
        }

        var endpoint = EndpointOf(site, settings);
        var key = settings.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            // The configuration's key for OpenAI or Anthropic is the operator's account there, so it goes to the provider's
            // own endpoint and to no other that a site names.
            var ownEndpointOnly = provider is AiProvider.OpenAi or AiProvider.Anthropic && endpoint is not null;
            key = ownEndpointOnly ? null : configuration[$"{AiOptions.Section}:{provider}:ApiKey"];
            if (string.IsNullOrWhiteSpace(key) && provider != AiProvider.Ollama)
            {
                throw NotConfigured(ownEndpointOnly
                    ? $"hold an API key, and the key of {AiOptions.Section}:{provider}:ApiKey is sent to the provider's own endpoint only"
                    : $"hold an API key, and {AiOptions.Section}:{provider}:ApiKey holds none");
            }
        }

        var timeout = TimeSpan.FromSeconds(settings.RequestTimeout);
        var client = provider switch
        {
            AiProvider.Anthropic => AnthropicClient(endpoint ?? AnthropicEndpoint, key!, model, timeout),
            AiProvider.Ollama => OllamaClient(endpoint!, key, model),
            _ => OpenAiClient(endpoint ?? OpenAiEndpoint, key!, model, timeout),
        };
        return ValueTask.FromResult<AiClient?>(new AiClient(client, NameOf(provider), model));
    }

    public void Dispose() => _handler.Dispose();

    // The allow-list is read at every call, so an endpoint saved before it left the list is refused too.
    private Uri? EndpointOf(SiteId site, AiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return settings.Provider is AiProvider.OpenAi or AiProvider.Anthropic ? null : throw NotConfigured("name an endpoint, which this provider needs");
        }

        var endpoint = AiProviderEndpoints.Parse(settings.Endpoint);
        if (endpoint is null || !AiProviderEndpoints.IsAllowed(endpoint, options.Value.AllowedEndpoints))
        {
            AiLog.EndpointRefused(logger, site.Value, endpoint?.Host ?? "(not a URL)");
            throw new PluginException(AiErrorCodes.EndpointNotAllowed, $"The AI endpoint of the site is not one of {AiOptions.Section}:AllowedEndpoints.");
        }

        return endpoint;
    }

    private IChatClient OpenAiClient(Uri endpoint, string key, string model, TimeSpan timeout)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = endpoint,
            Transport = new HttpClientPipelineTransport(Http(null)),
            NetworkTimeout = timeout,
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };
        return new OpenAIClient(new ApiKeyCredential(key), clientOptions).GetChatClient(model).AsIChatClient();
    }

    // The package's own spans are off: its environment variables can ask them to carry the messages.
    private IChatClient AnthropicClient(Uri endpoint, string key, string model, TimeSpan timeout) =>
        new Anthropic.AnthropicClient { ApiKey = key, BaseUrl = endpoint.ToString(), HttpClient = Http(null), MaxRetries = 0, Timeout = timeout, OpenTelemetry = false }.AsIChatClient(model);

    private OllamaApiClient OllamaClient(Uri endpoint, string? key, string model)
    {
        // The package asks for paths relative to the address, which keep the path of an endpoint that ends with a slash.
        var http = Http(new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/"));
        if (!string.IsNullOrWhiteSpace(key))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        return new OllamaApiClient(http, model);
    }

    // A client of a call over the shared handler, which holds the connections. The time a call may take is the plugin's.
    private HttpClient Http(Uri? baseAddress) => new(_handler, disposeHandler: false) { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };

    private static PluginException NotConfigured(string missing) =>
        new(AiErrorCodes.ProviderNotConfigured, $"The AI settings of the site do not {missing}.");
}

internal static class AiProviderFailures
{
    // The HTTP status a provider answered, where its package says. The message of the failure is never read.
    public static int? StatusOf(Exception exception) => exception switch
    {
        ClientResultException result => result.Status,
        HttpRequestException { StatusCode: { } status } => (int)status,
        _ => null,
    };
}
