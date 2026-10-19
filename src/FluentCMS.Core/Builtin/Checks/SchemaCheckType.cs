using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.Checks;

// The content matches its content type (CHK-01): every field the type requires holds a value, no field is one the type does not
// define, and every value is of the kind its field type takes. Entries are checked when they are written, so this catches drafts
// and content written before the type changed. 'required' names fields that must hold a value where the type leaves them
// optional, such as a hero image on product pages. Only the fields in the check's scope are checked.
internal sealed class SchemaCheckType(FieldTypeRegistry fieldTypes) : ICheckType
{
    public string Name => "schema";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);

        if (reader.OptionalStringList("required")?.FirstOrDefault(name => !SchemaNames.IsFieldName(name)) is { } invalid)
        {
            reader.Invalid("required", $"must hold field names such as 'hero', and '{invalid}' is not one");
        }

        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var subject = input.Subject;
        if (subject.Definition is not { } definition)
        {
            return Task.FromResult(CheckOutcome.NeedsReview(
                "The content names no content type, so it cannot be checked against one; give its content type."));
        }

        if (subject.Fields.ValueKind != JsonValueKind.Object)
        {
            return Task.FromResult(CheckOutcome.Fail("Give the fields as a JSON object with one property per field."));
        }

        var required = new CheckSettingsReader(Name, input.Settings).OptionalStringList("required") ?? [];
        var findings = new List<CheckFinding>();

        foreach (var field in definition.Fields.Where(field => field.Required || required.Contains(field.Name, StringComparer.Ordinal)))
        {
            if (!subject.Fields.TryGetProperty(field.Name, out var value))
            {
                findings.Add(new CheckFinding($"Add {field.Name}; {Requirer(field)} requires it."));
            }
            else if (!CheckContent.HasValue(value))
            {
                findings.Add(new CheckFinding($"Fill in {field.Name}; {Requirer(field)} requires it, and it is empty.", Pointer(field.Name)));
            }
        }

        foreach (var field in subject.Fields.EnumerateObject())
        {
            if (definition.Fields.FirstOrDefault(candidate => candidate.Name == field.Name) is not { } defined)
            {
                findings.Add(new CheckFinding($"Remove {field.Name}; the content type {subject.ContentType} has no such field.", Pointer(field.Name)));
                continue;
            }

            // The field alone and optional, since required fields are reported above.
            var single = new ContentTypeDefinition([new FieldDefinition(defined.Name, defined.Type, settings: defined.Settings)], definition.IsEntity);
            findings.AddRange(EntryFields.Validate(Document(field), single, fieldTypes).Errors
                .Select(error => new CheckFinding($"Change {field.Name}: {Unprefixed(error.Message, field.Name)}", Pointer(field.Name))));
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    private static string Requirer(FieldDefinition field)
    {
        return field.Required ? "the content type" : "this check";
    }

    private static string Pointer(string field)
    {
        return CheckContent.Append(string.Empty, field);
    }

    private static JsonElement Document(JsonProperty field)
    {
        using var document = JsonDocument.Parse($"{{{JsonSerializer.Serialize(field.Name)}: {field.Value.GetRawText()}}}");
        return document.RootElement.Clone();
    }

    private static string Unprefixed(string message, string field)
    {
        var prefix = $"Field '{field}': ";
        return message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] : message;
    }
}
