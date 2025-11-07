namespace FluentCMS.Api.Plugins.AIAgentManagement;

/// <summary>
/// Options for configuring an OpenRouter-backed chat client.
/// </summary>
public sealed class OpenRouterClientOptions
{
    /// <summary>
    /// OpenRouter API key (required). Example: "sk-or-v1-..."
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Default model slug (required). Example: "openrouter/auto" or "anthropic/claude-3.5-sonnet".
    /// </summary>
    public string Model { get; set; } = "openrouter/auto";

    /// <summary>
    /// OpenRouter base endpoint. Defaults to "https://openrouter.ai/api/v1".
    /// </summary>
    public Uri Endpoint { get; set; } = new Uri("https://openrouter.ai/api/v1");

    /// <summary>
    /// Optional attribution header. Use your site/app URL (scheme + host).
    /// OpenRouter recommends setting "HTTP-Referer" to your public URL for analytics.
    /// </summary>
    public string? AttributionReferer { get; set; }

    /// <summary>
    /// Optional attribution header. A short, human-friendly app title.
    /// OpenRouter recommends "X-Title" for analytics.
    /// </summary>
    public string? AttributionTitle { get; set; }

    /// <summary>
    /// Optional request timeout (HttpClient). Default: 100 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Set true to also include the standard "Referer" header alongside "HTTP-Referer".
    /// (Some proxies strip non-standard headers; this duplicates for safety.)
    /// </summary>
    public bool IncludeStandardReferrerHeader { get; set; } = true;
}

public static class OpenRouterServiceCollectionExtensions
{
    public static IServiceCollection AddOpenRouterChatClient(
        this IServiceCollection services,
        IConfiguration config,
        string sectionName = "OpenRouter")
    {
        var options = config.GetSection(sectionName);
        services.Configure<OpenRouterClientOptions>(options);
        services.TryAddSingleton<OpenRouterClient>();
        return services;
    }
}

public class OpenRouterClient(IOptions<OpenRouterClientOptions> options)
{
    public ChatClient GetChatClient()
    {
        var opt = options.Value;
        var aiClient = new OpenAIClientOptions
        {
            Endpoint = opt.Endpoint,
        };
        var credential = new ApiKeyCredential(opt.ApiKey);
        var client = new ChatClient(model: opt.Model, credential: credential, options: aiClient);
        return client;
    }
}
