using System.Buffers;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Schema;

// The fields of one content type version, stored as one JSON document (SCH-01, ADR-0005):
// {"entity":true,"description":"...","fields":[{"name":"price","type":"number","required":true,"description":"...","settings":{"min":0}}]}
public sealed class ContentTypeDefinition
{
    public const int MaxDescriptionLength = 1000;

    private static readonly JsonDocumentOptions ParseOptions = new() { AllowDuplicateProperties = false };

    public ContentTypeDefinition(IEnumerable<FieldDefinition> fields, bool isEntity = false, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(fields);

        Fields = fields.ToList();
        IsEntity = isEntity;
        Description = description;
        if (Fields.Any(field => field is null))
        {
            throw new ArgumentException("A definition cannot contain a null field.", nameof(fields));
        }
    }

    public IReadOnlyList<FieldDefinition> Fields { get; }

    // An entity type is a fact source: every one of its fields carries FactMetadata next to its value (SCH-03).
    public bool IsEntity { get; }

    // What the type is for, in the author's words; the schema document shows it to agents (SCH-06).
    public string? Description { get; }

    // Checks the shape of the document only; what the names, types and settings mean is checked by Validate.
    public static Result<ContentTypeDefinition> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ParseOptions);
        }
        catch (JsonException exception)
        {
            return Result<ContentTypeDefinition>.Failure(SchemaErrorCodes.DefinitionMalformed, $"The definition is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            var errors = new List<Error>();
            var (fields, isEntity, description) = ReadDocument(document.RootElement, errors);

            return errors.Count == 0
                ? Result<ContentTypeDefinition>.Success(new ContentTypeDefinition(fields, isEntity, description))
                : Result<ContentTypeDefinition>.Failure(errors);
        }
    }

    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("entity", IsEntity);
            WriteDescription(writer, Description);
            writer.WriteStartArray("fields");

            foreach (var field in Fields)
            {
                writer.WriteStartObject();
                writer.WriteString("name", field.Name);
                writer.WriteString("type", field.Type);
                writer.WriteBoolean("required", field.Required);
                WriteDescription(writer, field.Description);
                writer.WritePropertyName("settings");
                field.Settings.WriteTo(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // Every problem at once, each naming its field, so an agent can fix the definition in one round (UX-03).
    public Result Validate(FieldTypeRegistry fieldTypes)
    {
        ArgumentNullException.ThrowIfNull(fieldTypes);

        var errors = new List<Error>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!IsValidDescription(Description))
        {
            errors.Add(DescriptionInvalid("The type's description"));
        }

        for (var index = 0; index < Fields.Count; index++)
        {
            var field = Fields[index];
            var validName = SchemaNames.IsFieldName(field.Name);
            var label = validName ? $"Field '{field.Name}'" : $"Field {index + 1}";

            if (!validName)
            {
                errors.Add(new Error(
                    SchemaErrorCodes.FieldNameInvalid,
                    $"{label}: the name '{field.Name}' must start with a lowercase letter, contain only letters, digits and underscores, "
                    + $"and be at most {SchemaNames.MaxLength} characters."));
            }
            else if (!names.Add(field.Name))
            {
                errors.Add(new Error(
                    SchemaErrorCodes.FieldNameDuplicate,
                    $"{label}: another field already has this name; field names are compared ignoring case."));
            }

            if (!IsValidDescription(field.Description))
            {
                errors.Add(DescriptionInvalid($"{label}: the description"));
            }

            errors.AddRange(fieldTypes.ValidateSettings(field.Type, field.Settings)
                .Select(error => error with { Message = $"{label}: {error.Message}" }));
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    public bool HasField(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Fields.Any(field => field.Name == name);
    }

    // Only meaningful for a definition that passed Validate.
    public IReadOnlyList<FieldReference> References(FieldTypeRegistry fieldTypes)
    {
        ArgumentNullException.ThrowIfNull(fieldTypes);

        return Fields
            .SelectMany(field => fieldTypes.ReferenceTargets(field.Type, field.Settings).Select(target => new FieldReference(field.Name, target)))
            .ToList();
    }

    // A document without 'entity' is a plain content type, as every definition stored before the flag existed.
    private static (List<FieldDefinition> Fields, bool IsEntity, string? Description) ReadDocument(JsonElement root, List<Error> errors)
    {
        var fields = new List<FieldDefinition>();
        var isEntity = false;
        string? description = null;

        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Malformed("$ must be an object with a 'fields' array."));
            return (fields, isEntity, description);
        }

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "fields":
                    break;
                case "entity" when property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    isEntity = property.Value.GetBoolean();
                    break;
                case "entity":
                    errors.Add(Malformed("$.entity must be true or false."));
                    break;
                case "description":
                    description = ReadDescription(property.Value, "$.description", errors);
                    break;
                default:
                    errors.Add(Malformed($"$.{property.Name} is not part of a definition; it has 'entity', 'description' and 'fields'."));
                    break;
            }
        }

        if (!root.TryGetProperty("fields", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Malformed("$.fields must be an array."));
            return (fields, isEntity, description);
        }

        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            if (ReadField(element, $"$.fields[{index++}]", errors) is { } field)
            {
                fields.Add(field);
            }
        }

        return (fields, isEntity, description);
    }

    private static FieldDefinition? ReadField(JsonElement element, string path, List<Error> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Malformed($"{path} must be an object."));
            return null;
        }

        var count = errors.Count;
        string? name = null;
        string? type = null;
        var required = false;
        string? description = null;
        JsonElement? settings = null;

        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value;
            switch (property.Name)
            {
                case "name" when value.ValueKind == JsonValueKind.String:
                    name = value.GetString();
                    break;
                case "type" when value.ValueKind == JsonValueKind.String:
                    type = value.GetString();
                    break;
                case "required" when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    required = value.GetBoolean();
                    break;
                case "settings" when value.ValueKind == JsonValueKind.Object:
                    settings = value;
                    break;
                case "description":
                    description = ReadDescription(value, $"{path}.description", errors);
                    break;
                case "name" or "type":
                    errors.Add(Malformed($"{path}.{property.Name} must be a string."));
                    break;
                case "required":
                    errors.Add(Malformed($"{path}.required must be true or false."));
                    break;
                case "settings":
                    errors.Add(Malformed($"{path}.settings must be an object."));
                    break;
                default:
                    errors.Add(Malformed($"{path}.{property.Name} is not part of a field; a field has name, type, required, description and settings."));
                    break;
            }
        }

        if (name is null && !element.TryGetProperty("name", out _))
        {
            errors.Add(Malformed($"{path}.name is missing."));
        }

        if (type is null && !element.TryGetProperty("type", out _))
        {
            errors.Add(Malformed($"{path}.type is missing."));
        }

        return errors.Count == count ? new FieldDefinition(name!, type!, required, settings, description) : null;
    }

    // An explicit null reads as no description.
    private static string? ReadDescription(JsonElement value, string path, List<Error> errors)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString();
            case JsonValueKind.Null:
                return null;
            default:
                errors.Add(Malformed($"{path} must be a string."));
                return null;
        }
    }

    private static void WriteDescription(Utf8JsonWriter writer, string? description)
    {
        if (description is not null)
        {
            writer.WriteString("description", description);
        }
    }

    private static bool IsValidDescription(string? description)
    {
        return description is null || (!string.IsNullOrWhiteSpace(description) && description.Length <= MaxDescriptionLength);
    }

    private static Error DescriptionInvalid(string subject)
    {
        return new Error(
            SchemaErrorCodes.DescriptionInvalid,
            $"{subject} must not be blank and must be at most {MaxDescriptionLength} characters; leave it out instead.");
    }

    private static Error Malformed(string message)
    {
        return new Error(SchemaErrorCodes.DefinitionMalformed, message);
    }
}
