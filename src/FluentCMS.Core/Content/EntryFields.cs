using System.Buffers;
using System.Text.Json;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Content;

// The fields of one entry version as one JSON document with a property per field (CNT-02, ADR-0005), such as
// {"headline": "...", "product": "<entry id>"}. On an entity type every value is a fact with its metadata, in the shape the schema
// document describes (SCH-03, SCH-06).
public static class EntryFields
{
    private static readonly string FactProperties = string.Join(", ", FactMetadata.Properties.Prepend(FactMetadata.Value));

    // Every problem at once, each naming its field, so an agent can fix the document in one round (UX-03). The document returned is
    // the one to store: the fields in the definition's order, without explicit nulls, which read as absent values.
    public static Result<JsonElement> Validate(JsonElement fields, ContentTypeDefinition definition, FieldTypeRegistry fieldTypes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fieldTypes);

        if (fields.ValueKind != JsonValueKind.Object)
        {
            return Result<JsonElement>.Failure(
                ContentErrorCodes.FieldsMalformed,
                "The fields must be a JSON object with one property per field, such as {\"title\": \"...\"}.");
        }

        var errors = new List<Error>();
        var given = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var property in fields.EnumerateObject())
        {
            if (!definition.HasField(property.Name))
            {
                errors.Add(new Error(ContentErrorCodes.FieldUnknown, UnknownField(property.Name, definition)));
            }
            else if (!given.TryAdd(property.Name, property.Value))
            {
                errors.Add(new Error(ContentErrorCodes.FieldsMalformed, $"The field '{property.Name}' is given more than once."));
            }
        }

        var stored = new List<KeyValuePair<string, JsonElement>>();
        foreach (var field in definition.Fields)
        {
            if (!given.TryGetValue(field.Name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if (field.Required)
                {
                    errors.Add(new Error(ContentErrorCodes.FieldRequired, $"Field '{field.Name}' is required."));
                }

                continue;
            }

            var fieldErrors = definition.IsEntity
                ? FactErrors(value, field, fieldTypes)
                : fieldTypes.ValidateValue(field.Type, field.Settings, value);

            errors.AddRange(fieldErrors.Select(error => error with { Message = $"Field '{field.Name}': {error.Message}" }));
            stored.Add(new(field.Name, value));
        }

        return errors.Count == 0 ? Result<JsonElement>.Success(Write(stored)) : Result<JsonElement>.Failure(errors);
    }

    private static string UnknownField(string name, ContentTypeDefinition definition)
    {
        return definition.Fields.Count == 0
            ? $"There is no field '{name}'; this content type has no fields."
            : $"There is no field '{name}'; the fields are {string.Join(", ", definition.Fields.Select(field => field.Name))}.";
    }

    // Metadata may be null, so a fact says it has no source yet instead of leaving the property out (ENT-02).
    private static List<Error> FactErrors(JsonElement fact, FieldDefinition field, FieldTypeRegistry fieldTypes)
    {
        if (fact.ValueKind != JsonValueKind.Object)
        {
            return [Invalid($"A fact must be an object with {FactProperties}.")];
        }

        var errors = fact.EnumerateObject()
            .Where(property => property.Name != FactMetadata.Value && !FactMetadata.Properties.Contains(property.Name))
            .Select(property => Invalid($"A fact has no property '{property.Name}'; it has {FactProperties}."))
            .ToList();

        errors.AddRange(fact.EnumerateObject()
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => Invalid($"The fact's '{group.Key}' is given more than once.")));

        if (!fact.TryGetProperty(FactMetadata.Value, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            errors.Add(Invalid("The fact has no value; leave the field out instead."));
        }
        else
        {
            errors.AddRange(fieldTypes.ValidateValue(field.Type, field.Settings, value));
        }

        foreach (var name in FactMetadata.Properties)
        {
            if (!fact.TryGetProperty(name, out var metadata))
            {
                errors.Add(Invalid($"The fact's '{name}' is missing; give null when it has none."));
            }
            else if (metadata.ValueKind != JsonValueKind.Null && MetadataProblem(name, metadata) is { } problem)
            {
                errors.Add(Invalid(problem));
            }
        }

        return errors;
    }

    private static string? MetadataProblem(string name, JsonElement metadata)
    {
        return name switch
        {
            FactMetadata.Source => SourceProblem(metadata),
            FactMetadata.Owner => metadata.ValueKind == JsonValueKind.String && Guid.TryParseExact(metadata.GetString(), "D", out _)
                ? null
                : "The fact's owner must be the id of a user, a UUID, or null.",
            _ => metadata.ValueKind == JsonValueKind.String && IsoDateTimes.IsPointInTime(metadata.GetString())
                ? null
                : $"The fact's '{name}' must be a point in time in ISO 8601 with an offset, such as 2026-01-31T09:30:00Z, or null.",
        };
    }

    private static string? SourceProblem(JsonElement source)
    {
        var shape = $"The fact's source must be null or an object with '{FactMetadata.SourceKind}', one of "
            + $"{string.Join(", ", FactMetadata.SourceKinds)}, and '{FactMetadata.SourceReference}', the URL, file, person or system it names.";

        if (source.ValueKind != JsonValueKind.Object
            || source.EnumerateObject().Any(property => property.Name is not (FactMetadata.SourceKind or FactMetadata.SourceReference)))
        {
            return shape;
        }

        var kindValid = source.TryGetProperty(FactMetadata.SourceKind, out var kind)
            && kind.ValueKind == JsonValueKind.String
            && FactMetadata.SourceKinds.Contains(kind.GetString()!);
        var referenceValid = source.TryGetProperty(FactMetadata.SourceReference, out var reference)
            && reference.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(reference.GetString());

        return kindValid && referenceValid ? null : shape;
    }

    private static Error Invalid(string message)
    {
        return new Error(SchemaErrorCodes.ValueInvalid, message);
    }

    private static JsonElement Write(List<KeyValuePair<string, JsonElement>> fields)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in fields)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
