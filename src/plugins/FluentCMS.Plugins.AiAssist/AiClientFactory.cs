using System.ClientModel;
using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace FluentCMS.Plugins.AiAssist;

// The default of the IAiClientFactory slot: the client of the provider the site's settings name. OpenAI, Azure OpenAI and
// any OpenAI-compatible endpoint share the client of Microsoft.Extensions.AI.OpenAI. An endpoint that a site names is used
// only when it is one of FluentCms:Ai:AllowedEndpoints, and no redirect is followed, so that an allowed endpoint cannot
// lead the server elsewhere. A call is one request: the retries of the package are off, so that a call is paid for and
// logged once.
internal sealed class AiClientFactory(IOptions<AiOptions> options, IConfiguration configuration, IServiceScopeFactory scopes, ILogger<AiClientFactory> logger) : IAiClientFactory, IDisposable
{
    public static readonly Uri OpenAiEndpoint = new("https://api.openai.com/v1");

    // Shared by the sites, so it keeps no cookie a provider sets.
    private readonly SocketsHttpHandler _handler = new() { AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

    public static string NameOf(AiProvider provider) => JsonNamingPolicy.CamelCase.ConvertName(provider.ToString());

    public async ValueTask<AiClient?> CreateAsync(SiteId site, AiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var provider = settings.Provider;
        if (provider == AiProvider.None)
        {
            return null;
        }

        if (AiProviders.IsRemoved(provider))
        {
            throw AiProviders.Refuse(logger, site, provider);
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
            // The configuration's key for OpenAI is the operator's account there, so it goes to OpenAI's own endpoint and to
            // no other that a site names.
            var ownEndpointOnly = provider == AiProvider.OpenAi && endpoint is not null;
            key = ownEndpointOnly ? null : configuration[$"{AiOptions.Section}:{provider}:ApiKey"];
            if (string.IsNullOrWhiteSpace(key))
            {
                // An OpenAI-compatible endpoint may take no key, as Ollama's does.
                key = provider == AiProvider.OpenAiCompatible ? null : throw NotConfigured(ownEndpointOnly
                    ? $"hold an API key, and the key of {AiOptions.Section}:{provider}:ApiKey is sent to the provider's own endpoint only"
                    : $"hold an API key, and {AiOptions.Section}:{provider}:ApiKey holds none");
            }
        }
        else if (!await IsTheSitesOwnAsync(site, key, cancellationToken) && !await IsForTheDefaultsAsync(settings, endpoint, cancellationToken))
        {
            throw NotConfigured("hold an API key of the site's own: the key it inherits from the instance defaults is used with the provider and the endpoint of the defaults only");
        }

        var client = OpenAiClient(endpoint ?? OpenAiEndpoint, key, model);
        return new AiClient(client, NameOf(provider), model);
    }

    public void Dispose() => _handler.Dispose();

    // A site that does not set a key reads the key of the instance defaults (spec 4.13, "Settings as built"), which is the
    // operator's, for the provider and the endpoint the defaults name. The key is the site's own only when a read of the
    // site's settings says so and holds that same key: the settings the factory was given were read earlier, and a key that
    // was inherited then must not pass because the site has set a key of its own since.
    private async ValueTask<bool> IsTheSitesOwnAsync(SiteId site, string key, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var read = await scope.ServiceProvider.GetRequiredService<ISettings<AiSettings>>().ResolveAsync(site, cancellationToken);
        return !read.InheritedSecrets.Contains(nameof(AiSettings.ApiKey))
            && read.Value.ApiKey is { } own
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(own), Encoding.UTF8.GetBytes(key));
    }

    private async ValueTask<bool> IsForTheDefaultsAsync(AiSettings settings, Uri? endpoint, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var defaults = await scope.ServiceProvider.GetRequiredService<ISettings<AiSettings>>().GetDefaultAsync(cancellationToken);
        var theirs = AiProviderEndpoints.Parse(defaults.Endpoint);
        var sameEndpoint = theirs is null || endpoint is null
            ? theirs is null && endpoint is null
            : AiProviderEndpoints.Canonical(theirs) == AiProviderEndpoints.Canonical(endpoint);
        return defaults.Provider == settings.Provider && sameEndpoint;
    }

    // Against the list the host started with: a call is checked, not only a save, so an endpoint that was saved while it
    // was on the list is refused once the host runs with a list that no longer holds it.
    private Uri? EndpointOf(SiteId site, AiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return settings.Provider == AiProvider.OpenAi ? null : throw NotConfigured("name an endpoint, which this provider needs");
        }

        var endpoint = AiProviderEndpoints.Parse(settings.Endpoint);
        if (endpoint is null || !AiProviderEndpoints.IsAllowed(endpoint, options.Value.AllowedEndpoints))
        {
            AiLog.EndpointRefused(logger, site.Value, endpoint?.Host ?? "(not a URL)");
            throw new PluginException(AiErrorCodes.EndpointNotAllowed, $"The AI endpoint of the site is not one of {AiOptions.Section}:AllowedEndpoints.");
        }

        return endpoint;
    }

    // A client of a call over the shared handler, which holds the connections. The time a call may take is the plugin's
    // alone: a timeout of the package's own, even of the same length, races it, and a call that passed requestTimeout would
    // then end as a failure of the provider.
    private IChatClient OpenAiClient(Uri endpoint, string? key, string model)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = endpoint,
            Transport = new HttpClientPipelineTransport(new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan }),
            NetworkTimeout = Timeout.InfiniteTimeSpan,
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };

        // The package takes no client without a credential, so an endpoint that takes no key gets the request without the
        // header of a placeholder.
        if (key is null)
        {
            clientOptions.AddPolicy(new NoKey(), PipelinePosition.BeforeTransport);
        }

        return new OpenAIClient(new ApiKeyCredential(key ?? NoKey.Placeholder), clientOptions).GetChatClient(model).AsIChatClient();
    }

    private static PluginException NotConfigured(string missing) =>
        new(AiErrorCodes.ProviderNotConfigured, $"The AI settings of the site do not {missing}.");

    private sealed class NoKey : PipelinePolicy
    {
        public const string Placeholder = "no-key";

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Remove("Authorization");
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Remove("Authorization");
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }
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
