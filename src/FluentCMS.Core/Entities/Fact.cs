using System.Text.Json;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Entities;

// One fact of an entity version: a field's metadata as the entity stores it (SCH-03). The value stays in the version's document.
public sealed record Fact(
    string Field,
    string? SourceKind,
    string? SourceReference,
    Guid? Owner,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil,
    DateTimeOffset? LastVerified)
{
    // A fact without a source is kept and flagged, so routing can treat it as risky or blocking (ENT-02).
    public bool IsUnsourced => SourceKind is null;

    // The facts of an entity document that passed EntryFields.Validate, in the document's order.
    public static IReadOnlyList<Fact> ReadAll(JsonElement fields)
    {
        return fields.EnumerateObject().Select(property => Read(property.Name, property.Value)).ToList();
    }

    private static Fact Read(string field, JsonElement fact)
    {
        var source = fact.GetProperty(FactMetadata.Source);
        var hasSource = source.ValueKind == JsonValueKind.Object;

        return new Fact(
            field,
            hasSource ? source.GetProperty(FactMetadata.SourceKind).GetString() : null,
            hasSource ? source.GetProperty(FactMetadata.SourceReference).GetString() : null,
            fact.GetProperty(FactMetadata.Owner) is { ValueKind: JsonValueKind.String } owner ? owner.GetGuid() : null,
            Time(fact, FactMetadata.ValidFrom),
            Time(fact, FactMetadata.ValidUntil),
            Time(fact, FactMetadata.LastVerified));
    }

    private static DateTimeOffset? Time(JsonElement fact, string name)
    {
        return fact.GetProperty(name) is { ValueKind: JsonValueKind.String } value ? value.GetDateTimeOffset() : null;
    }
}
