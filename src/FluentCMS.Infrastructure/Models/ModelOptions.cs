using FluentCMS.Core.Models;

namespace FluentCMS.Infrastructure.Models;

// Models__Profiles__checks__Provider=openai, Models__Profiles__checks__Model=..., Models__Profiles__checks__ApiKey=... (ADR-0008).
public sealed class ModelOptions
{
    public const string SectionName = "Models";

    public Dictionary<string, ModelProfileOptions> Profiles { get; set; } = new(StringComparer.Ordinal);

    // How long the verbatim prompt and response of a call are kept (AUD-04).
    public TimeSpan PromptRetention { get; set; } = TimeSpan.FromDays(90);
}

public sealed class ModelProfileOptions
{
    // "openai" for any OpenAI-compatible endpoint, or "anthropic".
    public string? Provider { get; set; }

    // The provider's own endpoint when left out.
    public string? Endpoint { get; set; }

    public string? Model { get; set; }

    // From configuration or the environment, never from code; a local server may need none.
    public string? ApiKey { get; set; }

    public decimal InputCostPerMillionTokens { get; set; }

    public decimal OutputCostPerMillionTokens { get; set; }

    public int MaxOutputTokens { get; set; } = 1024;

    public float? Temperature { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    public Uri DefaultEndpoint()
    {
        return new Uri(Provider == ModelProviders.Anthropic ? "https://api.anthropic.com" : "https://api.openai.com/v1");
    }
}

// Outbound__AllowedHosts__0=api.openai.com: the hosts requests may go to (SEC-05). A host also allows its subdomains.
public sealed class OutboundOptions
{
    public const string SectionName = "Outbound";

    public List<string> AllowedHosts { get; set; } = [];
}
