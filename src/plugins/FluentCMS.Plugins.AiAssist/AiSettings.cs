using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FluentCMS.Settings;

namespace FluentCMS.Plugins.AiAssist;

// The settings area "ai" (settings.ai.manage), per site with instance defaults. AI assist is off until a site names a
// provider and opts fields in.
[Settings("ai")]
public sealed class AiSettings : IValidatableObject
{
    public const int MaxTypes = 200;
    public const int MaxFieldsPerType = 100;
    public const int NameMaxLength = 64;

    [JsonConverter(typeof(AiProviderJsonConverter))]
    public AiProvider Provider { get; set; } = AiProvider.None;

    // The model, or for Azure OpenAI the deployment.
    [StringLength(128)]
    public string? Model { get; set; }

    // One of FluentCms:Ai:AllowedEndpoints. OpenAI uses its own endpoint when it is unset; the other providers need one.
    [StringLength(2048)]
    public string? Endpoint { get; set; }

    // Unset, the key comes from FluentCms:Ai:{Provider}:ApiKey. An OpenAI-compatible endpoint, such as Ollama's, may take none.
    [SettingsSecret]
    [StringLength(512)]
    public string? ApiKey { get; set; }

    [Range(1, 32000)]
    public int MaxOutputTokens { get; set; } = 1024;

    // In seconds.
    [Range(1, 600)]
    public int RequestTimeout { get; set; } = 60;

    // The calls a principal may make in a minute in the site, counted on each instance.
    [Range(1, 600)]
    public int CallsPerMinute { get; set; } = 20;

    // The opt-in: the fields that take actions, by content type.
    public Dictionary<string, List<string>> Fields { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AiProviders.IsRemoved(Provider))
        {
            yield return new ValidationResult($"The plugin no longer has this provider. {AiProviders.Instead}", [nameof(Provider)]);
        }
        else if (!Enum.IsDefined(Provider))
        {
            yield return new ValidationResult("The provider is none, openAi, azureOpenAi or openAiCompatible.", [nameof(Provider)]);
        }

        if (!string.IsNullOrWhiteSpace(Endpoint) && AiProviderEndpoints.Parse(Endpoint) is null)
        {
            yield return new ValidationResult(AiProviderEndpoints.Shape, [nameof(Endpoint)]);
        }

        if (Fields is null || Fields.Count > MaxTypes)
        {
            yield return new ValidationResult($"The fields are a map of at most {MaxTypes} content types, empty for none.", [nameof(Fields)]);
        }
        else if (Fields.Any(entry => !IsName(entry.Key) || entry.Value is null || entry.Value.Count > MaxFieldsPerType || !entry.Value.All(IsName)))
        {
            yield return new ValidationResult($"Each entry names a content type and up to {MaxFieldsPerType} of its fields, each name of 1 to {NameMaxLength} characters.", [nameof(Fields)]);
        }
    }

    private static bool IsName(string? name) => name is { Length: > 0 and <= NameMaxLength } && !name.Any(char.IsWhiteSpace);
}

public enum AiProvider
{
    // No provider: AI assist is off.
    None,

    OpenAi,

    // Through its OpenAI-compatible endpoint, with the deployment as the model and an API key.
    AzureOpenAi,

    // Any other endpoint that speaks the OpenAI chat completions API, such as Ollama's /v1 or a gateway. 3 and 4 were
    // Anthropic and Ollama, which settings may still name.
    OpenAiCompatible = 5,
}
