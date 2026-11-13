using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Sites;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.AiAssist;

// Anthropic and Ollama, which the plugin had until it was cut to OpenAI-compatible endpoints (spec 10.4, H2). Settings and
// configuration that still name one are refused with what to use instead.
internal static class AiProviders
{
    public const string Instead =
        "Use openAiCompatible: Ollama at its /v1 endpoint, such as http://localhost:11434/v1, which needs no key, and a model "
        + "that has no OpenAI-compatible endpoint through a gateway that gives it one. A host can bring any other client by "
        + "filling the IAiClientFactory slot.";

    // By the names settings gave them, with the numbers they had in AiProvider, which no provider takes again.
    private static readonly Dictionary<string, AiProvider> Removed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["anthropic"] = (AiProvider)3,
        ["ollama"] = (AiProvider)4,
    };

    public static bool IsRemoved(AiProvider provider) => Removed.ContainsValue(provider);

    public static bool TryParseRemoved(string? name, out AiProvider provider) => Removed.TryGetValue(name ?? "", out provider);

    public static string? RemovedName(AiProvider provider) => Removed.FirstOrDefault(pair => pair.Value == provider).Key;

    // The refusal of a call in a site whose settings name a removed provider, logged so that the operator finds the site.
    public static PluginException Refuse(ILogger logger, SiteId site, AiProvider provider)
    {
        AiLog.ProviderRemoved(logger, site.Value, RemovedName(provider)!);
        return new PluginException(AiErrorCodes.ProviderRemoved, $"The AI settings of the site name a provider the plugin no longer has. {Instead}");
    }

    // A section such as FluentCms:Ai:Anthropic, which held the key of a removed provider.
    public static bool AreNamedIn(IConfiguration configuration) =>
        Removed.Keys.Any(name => configuration.GetSection($"{AiOptions.Section}:{name}").Exists());
}

// The provider as core's settings read and write it, and also by the name of a removed provider. Settings that still name
// one are then read and refused with what to use, where the site would otherwise have its settings skipped as unreadable
// and follow the instance defaults without a word.
internal sealed class AiProviderJsonConverter : JsonConverter<AiProvider>
{
    private static readonly JsonStringEnumConverter<AiProvider> Names = new(JsonNamingPolicy.CamelCase);

    private JsonConverter<AiProvider>? _names;

    public override AiProvider Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && AiProviders.TryParseRemoved(reader.GetString(), out var removed)
            ? removed
            : NamesFor(options).Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, AiProvider value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (AiProviders.RemovedName(value) is { } removed)
        {
            writer.WriteStringValue(removed);
        }
        else
        {
            NamesFor(options).Write(writer, value, options);
        }
    }

    private JsonConverter<AiProvider> NamesFor(JsonSerializerOptions options) =>
        _names ??= (JsonConverter<AiProvider>)Names.CreateConverter(typeof(AiProvider), options);
}
