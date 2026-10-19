using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Schema;

namespace FluentCMS.Admin.Forms;

// The fact around an entity field's value (SCH-03): where it comes from, who owns it and when it holds. A generated form posts each
// part as text under the field's name, such as "price.kind"; this turns them into the fact object entities store, and back.
public static class FactValues
{
    public const string Kind = "kind";
    public const string Reference = "reference";
    public const string Owner = "owner";

    public static IReadOnlyList<string> Times { get; } = [FactMetadata.ValidFrom, FactMetadata.ValidUntil, FactMetadata.LastVerified];

    public static string Key(string field, string part) => $"{field}.{part}";

    // The fact for a value, or null with the problems of each part by part name.
    public static JsonObject? Read(string field, JsonNode value, IReadOnlyDictionary<string, string?> posted, Dictionary<string, string> problems)
    {
        string? Part(string part) => posted.GetValueOrDefault(Key(field, part)) is { } text && text.Trim().Length > 0 ? text.Trim() : null;

        var kind = Part(Kind);
        var reference = Part(Reference);
        if (kind is not null && !FactMetadata.SourceKinds.Contains(kind))
        {
            problems[Kind] = "kind";
        }
        else if ((kind is null) != (reference is null))
        {
            problems[kind is null ? Kind : Reference] = "source";
        }

        Guid? owner = null;
        if (Part(Owner) is { } ownerText)
        {
            if (Guid.TryParse(ownerText, out var ownerId))
            {
                owner = ownerId;
            }
            else
            {
                problems[Owner] = "owner";
            }
        }

        var times = new Dictionary<string, string?>();
        foreach (var name in Times)
        {
            times[name] = null;
            if (Part(name) is not { } text)
            {
                continue;
            }

            if (DateTime.TryParseExact(text, [FieldValues.DateTimeFormat, FieldValues.DateTimeFormat + ":ss"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
            {
                times[name] = time.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            }
            else
            {
                problems[name] = "dateTime";
            }
        }

        if (problems.Count > 0)
        {
            return null;
        }

        return new JsonObject
        {
            [FactMetadata.Value] = value.DeepClone(),
            [FactMetadata.Source] = kind is null ? null : new JsonObject { [FactMetadata.SourceKind] = kind, [FactMetadata.SourceReference] = reference },
            [FactMetadata.Owner] = owner?.ToString(),
            [FactMetadata.ValidFrom] = times[FactMetadata.ValidFrom],
            [FactMetadata.ValidUntil] = times[FactMetadata.ValidUntil],
            [FactMetadata.LastVerified] = times[FactMetadata.LastVerified],
        };
    }

    // The stored fact's value for the value widget, and its parts for the metadata widgets.
    public static JsonElement? Show(string field, JsonElement? stored, IDictionary<string, string?> facts)
    {
        if (stored is not { ValueKind: JsonValueKind.Object } fact)
        {
            return null;
        }

        if (fact.TryGetProperty(FactMetadata.Source, out var source) && source.ValueKind == JsonValueKind.Object)
        {
            facts[Key(field, Kind)] = source.GetProperty(FactMetadata.SourceKind).GetString();
            facts[Key(field, Reference)] = source.GetProperty(FactMetadata.SourceReference).GetString();
        }

        if (fact.TryGetProperty(FactMetadata.Owner, out var owner) && owner.ValueKind == JsonValueKind.String)
        {
            facts[Key(field, Owner)] = owner.GetString();
        }

        foreach (var name in Times)
        {
            if (fact.TryGetProperty(name, out var time) && time.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                facts[Key(field, name)] = parsed.UtcDateTime.ToString(FieldValues.DateTimeFormat, CultureInfo.InvariantCulture);
            }
        }

        return fact.TryGetProperty(FactMetadata.Value, out var value) ? value : null;
    }

    public static bool IsUnsourced(JsonElement? stored)
    {
        return stored is { ValueKind: JsonValueKind.Object } fact
            && (!fact.TryGetProperty(FactMetadata.Source, out var source) || source.ValueKind != JsonValueKind.Object);
    }
}
