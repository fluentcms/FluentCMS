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
    // A time that shows the same as the stored fact's keeps its stored text, so its seconds are not lost to the widget.
    // An owner must be one of the known users when they are given.
    public static JsonObject? Read(
        string field,
        JsonNode value,
        IReadOnlyDictionary<string, string?> posted,
        Dictionary<string, string> problems,
        JsonElement? stored = null,
        IReadOnlySet<Guid>? owners = null)
    {
        var shown = new Dictionary<string, string?>();
        Show(field, stored, shown);

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

        string? owner = null;
        if (Part(Owner) is { } ownerText)
        {
            if (Guid.TryParse(ownerText, out var ownerId) && (owners is null || owners.Contains(ownerId)))
            {
                // The stored text is kept when it names the same user, whatever its case.
                owner = stored is { } fact && fact.TryGetProperty(FactMetadata.Owner, out var storedOwner) && storedOwner.ValueKind == JsonValueKind.String
                    && Guid.TryParse(storedOwner.GetString(), out var storedId) && storedId == ownerId
                        ? storedOwner.GetString()
                        : ownerId.ToString();
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

            if (text == shown.GetValueOrDefault(Key(field, name)) && stored is { } fact && fact.GetProperty(name).GetString() is { } kept)
            {
                times[name] = kept;
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
            [FactMetadata.Owner] = owner,
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

        // Compared as a GUID, so an id stored in another case still selects its user.
        if (fact.TryGetProperty(FactMetadata.Owner, out var owner) && owner.ValueKind == JsonValueKind.String)
        {
            facts[Key(field, Owner)] = Guid.TryParse(owner.GetString(), out var ownerId) ? ownerId.ToString() : owner.GetString();
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

    // Whether any part of the field's fact was given, such as a source typed for a value left empty.
    public static bool HasDetails(string field, IReadOnlyDictionary<string, string?> posted)
    {
        return new[] { Kind, Reference, Owner }.Concat(Times).Any(part => !string.IsNullOrWhiteSpace(posted.GetValueOrDefault(Key(field, part))));
    }

    public static bool IsUnsourced(JsonElement? stored)
    {
        return stored is { ValueKind: JsonValueKind.Object } fact
            && (!fact.TryGetProperty(FactMetadata.Source, out var source) || source.ValueKind != JsonValueKind.Object);
    }
}
