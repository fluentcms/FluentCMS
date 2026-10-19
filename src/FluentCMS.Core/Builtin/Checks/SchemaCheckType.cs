using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.Checks;

// The content matches its content type: no field the type does not define, and every value of the kind its field type takes
// (CHK-01). Entries are checked when they are written, so this catches drafts and content written before the type changed.
// 'required' names fields that must hold a value even where the type leaves them optional, such as a hero image on product pages.
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
        var required = new CheckSettingsReader(Name, input.Settings).OptionalStringList("required") ?? [];
        var findings = Missing(subject.Fields, required);

        if (subject.Definition is not { } definition)
        {
            return Task.FromResult(required.Count == 0
                ? CheckOutcome.NeedsReview("The content names no content type, so it cannot be checked against one; give its content type.")
                : Outcome(findings));
        }

        if (subject.Fields.ValueKind != JsonValueKind.Object)
        {
            return Task.FromResult(CheckOutcome.Fail("Give the fields as a JSON object with one property per field."));
        }

        foreach (var field in subject.Fields.EnumerateObject())
        {
            var pointer = CheckContent.Append(string.Empty, field.Name);
            if (definition.Fields.FirstOrDefault(candidate => candidate.Name == field.Name) is not { } defined)
            {
                findings.Add(new CheckFinding($"Remove {field.Name}; the content type {subject.ContentType} has no such field.", pointer));
                continue;
            }

            // The field alone, so a required field the check's scope leaves out is not reported missing.
            var single = new ContentTypeDefinition([new FieldDefinition(defined.Name, defined.Type, settings: defined.Settings)], definition.IsEntity);
            findings.AddRange(EntryFields.Validate(Document(field), single, fieldTypes).Errors
                .Select(error => new CheckFinding($"Change {field.Name}: {Unprefixed(error.Message, field.Name)}", pointer)));
        }

        return Task.FromResult(Outcome(findings));
    }

    private static List<CheckFinding> Missing(JsonElement fields, IReadOnlyList<string> required)
    {
        var findings = new List<CheckFinding>();
        foreach (var name in required)
        {
            if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(name, out var value))
            {
                findings.Add(new CheckFinding($"Add {name}; this check requires it."));
            }
            else if (!CheckContent.HasValue(value))
            {
                findings.Add(new CheckFinding($"Fill in {name}; this check requires it, and it is empty.", CheckContent.Append(string.Empty, name)));
            }
        }

        return findings;
    }

    private static CheckOutcome Outcome(List<CheckFinding> findings)
    {
        return findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings);
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
