using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCMS.Core.Settings;

// One reading of the JSON of settings, used everywhere so that a document means the same on the way in and on the way out.
internal static class SettingsJson
{
    public const int MaxBytes = 64 * 1024;
    public const int MaxDepth = 16;

    // For what is written: a key that the type does not have, or a repeated one, is an error.
    public static readonly JsonSerializerOptions Strict = Create(strict: true);

    // For what is read: a document written by an earlier version of the class may hold keys that are gone, and that must not
    // make the site unreadable.
    public static readonly JsonSerializerOptions Lenient = Create(strict: false);

    private static JsonSerializerOptions Create(bool strict)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = strict ? JsonUnmappedMemberHandling.Disallow : JsonUnmappedMemberHandling.Skip,
            AllowDuplicateProperties = !strict,
            RespectNullableAnnotations = true,
            MaxDepth = MaxDepth,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
