using System.Buffers;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Checks;

// One version of a check, stored as one JSON document (ADR-0005):
// {"type":"terms","description":"...","severity":"block","settings":{...},"scope":{"contentTypes":["product-page"],"fields":["body"]},
//  "examples":[{"name":"...","expect":"fail","contentType":"product-page","fields":{...},"sources":["..."]}]}
public sealed class CheckDefinition
{
    public const int MaxDescriptionLength = 1000;
    public const int MaxExamples = 50;

    private static readonly JsonDocumentOptions ParseOptions = new() { AllowDuplicateProperties = false };

    public CheckDefinition(
        string type,
        string description,
        JsonElement? settings,
        IEnumerable<CheckExample> examples,
        CheckSeverity severity = CheckSeverity.Block,
        CheckScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(examples);

        Type = type;
        Description = description;
        Settings = settings is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } given ? given.Clone() : FieldDefinition.EmptySettings;
        Examples = examples.ToList();
        Severity = severity;
        Scope = scope ?? CheckScope.All;
        if (Examples.Any(example => example is null))
        {
            throw new ArgumentException("A definition cannot contain a null example.", nameof(examples));
        }
    }

    public string Type { get; }

    // What the check enforces, in the author's words; shown next to its findings so a failure reads in context (UX-03).
    public string Description { get; }

    // Read by the check type named in Type.
    public JsonElement Settings { get; }

    public IReadOnlyList<CheckExample> Examples { get; }

    public CheckSeverity Severity { get; }

    public CheckScope Scope { get; }

    // Checks the shape of the document only; what the type, settings and examples mean is checked by Validate.
    public static Result<CheckDefinition> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ParseOptions);
        }
        catch (JsonException exception)
        {
            return Result<CheckDefinition>.Failure(CheckErrorCodes.DefinitionMalformed, $"The definition is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            var errors = new List<Error>();
            CheckDefinition? definition;
            try
            {
                definition = ReadDocument(document.RootElement, errors);
            }
            catch (InvalidOperationException)
            {
                // A string escapes half of a surrogate pair, which no .NET string can hold.
                return Result<CheckDefinition>.Failure(CheckErrorCodes.DefinitionMalformed, "The definition holds text that is not valid Unicode.");
            }

            return errors.Count == 0 ? Result<CheckDefinition>.Success(definition!) : Result<CheckDefinition>.Failure(errors);
        }
    }

    // Read again from the text, so a duplicate property is refused here as it is in a stored document.
    public static Result<CheckDefinition> Parse(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Undefined
            ? Result<CheckDefinition>.Failure(CheckErrorCodes.DefinitionMalformed, "$ must be an object with 'type', 'description' and 'examples'.")
            : Parse(root.GetRawText());
    }

    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", Type);
            writer.WriteString("description", Description);
            writer.WriteString("severity", Severity == CheckSeverity.Warn ? "warn" : "block");
            writer.WritePropertyName("settings");
            Settings.WriteTo(writer);

            if (Scope.ContentTypes is not null || Scope.Fields is not null)
            {
                writer.WriteStartObject("scope");
                WriteNames(writer, "contentTypes", Scope.ContentTypes);
                WriteNames(writer, "fields", Scope.Fields);
                writer.WriteEndObject();
            }

            writer.WriteStartArray("examples");
            foreach (var example in Examples)
            {
                writer.WriteStartObject();
                writer.WriteString("name", example.Name);
                writer.WriteString("expect", example.Expected == CheckVerdict.Pass ? "pass" : "fail");
                if (example.ContentType is not null)
                {
                    writer.WriteString("contentType", example.ContentType);
                }

                writer.WritePropertyName("fields");
                example.Fields.WriteTo(writer);
                WriteNames(writer, "sources", example.Sources);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // Every problem at once, each naming its setting or example, so the author fixes the check in one round (UX-03).
    public Result Validate(CheckTypeRegistry checkTypes)
    {
        ArgumentNullException.ThrowIfNull(checkTypes);

        var errors = new List<Error>(checkTypes.ValidateSettings(Type, Settings));

        if (string.IsNullOrWhiteSpace(Description) || Description.Length > MaxDescriptionLength)
        {
            errors.Add(new Error(
                CheckErrorCodes.DescriptionInvalid,
                $"The description must say what the check enforces, in at most {MaxDescriptionLength} characters."));
        }

        if (!Enum.IsDefined(Severity))
        {
            errors.Add(new Error(CheckErrorCodes.SeverityInvalid, $"The severity '{Severity}' is not one of block or warn."));
        }

        errors.AddRange(ScopeErrors("contentTypes", Scope.ContentTypes, SchemaNames.IsSlug, "a content type slug such as 'product-page'"));
        errors.AddRange(ScopeErrors("fields", Scope.Fields, SchemaNames.IsFieldName, "a field name such as 'body'"));
        errors.AddRange(ExampleErrors());

        if (errors.Count == 0)
        {
            errors.AddRange(StorageErrors());
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    // Runs every example with these settings and scope and reports each one that does not get its verdict, by name (CHK-02).
    // The definitions are those of the content types the examples name; only meaningful for a definition that passed Validate.
    public async Task<Result> RunExamplesAsync(
        CheckTypeRegistry checkTypes,
        IReadOnlyDictionary<string, ContentTypeDefinition> contentTypes,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkTypes);
        ArgumentNullException.ThrowIfNull(contentTypes);
        ArgumentNullException.ThrowIfNull(services);

        var errors = new List<Error>();
        foreach (var example in Examples)
        {
            var definition = example.ContentType is { } slug ? contentTypes.GetValueOrDefault(slug) : null;
            var subject = Scope.Narrow(new CheckSubject(example.Fields, example.Sources, example.ContentType, definition));
            var outcome = await checkTypes.EvaluateAsync(Type, new CheckInput(Settings, subject, services), cancellationToken);

            if (Mismatch(example, outcome) is { } message)
            {
                errors.Add(new Error(CheckErrorCodes.ExampleFailed, message));
            }
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    // A definition that passes Validate reads back as it is stored, so a duplicate property, which a definition built in code can
    // hold, is refused instead of making the stored check unreadable.
    private IReadOnlyList<Error> StorageErrors()
    {
        return Parse(ToJson()).Errors;
    }

    private static string? Mismatch(CheckExample example, CheckOutcome outcome)
    {
        var first = outcome.Findings.FirstOrDefault()?.Message;
        var more = outcome.Findings.Count > 1 ? $" (and {outcome.Findings.Count - 1} more)" : string.Empty;

        return (example.Expected, outcome.Verdict) switch
        {
            (CheckVerdict.Pass, CheckVerdict.Pass) or (CheckVerdict.Fail, CheckVerdict.Fail) => null,
            (_, CheckVerdict.NeedsReview) => $"Example '{example.Name}' could not be decided: {first}",
            (CheckVerdict.Pass, _) => $"Example '{example.Name}' should pass, but the check failed it: {first}{more}",
            _ => $"Example '{example.Name}' should fail, but the check passed it.",
        };
    }

    private List<Error> ExampleErrors()
    {
        var errors = new List<Error>();

        if (!Examples.Any(example => example.Expected == CheckVerdict.Pass) || !Examples.Any(example => example.Expected == CheckVerdict.Fail))
        {
            errors.Add(new Error(
                CheckErrorCodes.ExamplesMissing,
                "A check needs at least one example it must pass and one it must fail, so its examples show what it accepts and what it refuses."));
        }

        if (Examples.Count > MaxExamples)
        {
            errors.Add(new Error(CheckErrorCodes.ExampleInvalid, $"A check has at most {MaxExamples} examples, and this one has {Examples.Count}."));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Examples.Count; index++)
        {
            var example = Examples[index];
            var validName = !string.IsNullOrWhiteSpace(example.Name) && example.Name.Length <= CheckExample.MaxNameLength;
            var label = validName ? $"Example '{example.Name}'" : $"Example {index + 1}";

            if (!validName)
            {
                errors.Add(ExampleInvalid(label, $"needs a name of at most {CheckExample.MaxNameLength} characters that says what it shows"));
            }
            else if (!names.Add(example.Name))
            {
                errors.Add(ExampleInvalid(label, "has the same name as another example; names are compared ignoring case"));
            }

            if (example.Expected is not (CheckVerdict.Pass or CheckVerdict.Fail))
            {
                errors.Add(ExampleInvalid(label, "must expect the check to pass or fail"));
            }

            if (example.Fields.ValueKind != JsonValueKind.Object)
            {
                errors.Add(ExampleInvalid(label, "must give its fields as a JSON object with one property per field"));
            }

            if (example.Sources.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add(ExampleInvalid(label, "has a blank source"));
            }

            if (example.ContentType is null && Scope.ContentTypes is not null)
            {
                errors.Add(ExampleInvalid(
                    label,
                    $"names no content type, but the check applies only to {string.Join(", ", Scope.ContentTypes.Distinct())}; name one of them"));
            }
            else if (example.ContentType is { } slug)
            {
                if (!SchemaNames.IsSlug(slug))
                {
                    errors.Add(ExampleInvalid(label, $"names the content type '{slug}', which is not a slug such as 'product-page'"));
                }
                else if (!Scope.AppliesTo(slug))
                {
                    errors.Add(ExampleInvalid(
                        label,
                        $"is of the content type '{slug}', which the check does not apply to; use one of {string.Join(", ", Scope.ContentTypes!.Distinct())}"));
                }
            }
        }

        return errors;
    }

    private static IEnumerable<Error> ScopeErrors(string name, IReadOnlyList<string>? values, Func<string?, bool> isValid, string expected)
    {
        if (values is null)
        {
            yield break;
        }

        if (values.Count == 0)
        {
            yield return new Error(CheckErrorCodes.ScopeInvalid, $"The scope's {name} must not be empty; leave it out to apply to all of them.");
            yield break;
        }

        foreach (var value in values.Where(value => !isValid(value)).Distinct())
        {
            yield return new Error(CheckErrorCodes.ScopeInvalid, $"The scope's {name} hold '{value}', which is not {expected}.");
        }

        foreach (var value in values.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            yield return new Error(CheckErrorCodes.ScopeInvalid, $"The scope's {name} hold '{value}' more than once.");
        }
    }

    private static Error ExampleInvalid(string label, string problem)
    {
        return new Error(CheckErrorCodes.ExampleInvalid, $"{label} {problem}.");
    }

    private static void WriteNames(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static CheckDefinition? ReadDocument(JsonElement root, List<Error> errors)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Malformed("$ must be an object with 'type', 'description' and 'examples'."));
            return null;
        }

        var count = errors.Count;
        string? type = null;
        string? description = null;
        var severity = CheckSeverity.Block;
        JsonElement? settings = null;
        var scope = CheckScope.All;
        var examples = new List<CheckExample>();

        foreach (var property in root.EnumerateObject())
        {
            var value = property.Value;
            switch (property.Name)
            {
                case "type":
                    type = ReadString(value, "$.type", errors);
                    break;
                case "description":
                    description = ReadString(value, "$.description", errors);
                    break;
                case "severity" when value.ValueKind == JsonValueKind.String && value.GetString() is "block" or "warn":
                    severity = value.GetString() == "warn" ? CheckSeverity.Warn : CheckSeverity.Block;
                    break;
                case "severity":
                    errors.Add(Malformed("$.severity must be \"block\" or \"warn\"."));
                    break;
                case "settings" when value.ValueKind == JsonValueKind.Object:
                    settings = value;
                    break;
                case "settings":
                    errors.Add(Malformed("$.settings must be an object."));
                    break;
                case "scope":
                    scope = ReadScope(value, errors);
                    break;
                case "examples" when value.ValueKind == JsonValueKind.Array:
                    var index = 0;
                    foreach (var element in value.EnumerateArray())
                    {
                        if (ReadExample(element, $"$.examples[{index++}]", errors) is { } example)
                        {
                            examples.Add(example);
                        }
                    }

                    break;
                case "examples":
                    errors.Add(Malformed("$.examples must be an array."));
                    break;
                default:
                    errors.Add(Malformed(
                        $"$.{property.Name} is not part of a check; it has 'type', 'description', 'severity', 'settings', 'scope' and 'examples'."));
                    break;
            }
        }

        foreach (var required in new[] { "type", "description", "examples" }.Where(name => !root.TryGetProperty(name, out _)))
        {
            errors.Add(Malformed($"$.{required} is missing."));
        }

        return errors.Count == count ? new CheckDefinition(type!, description!, settings, examples, severity, scope) : null;
    }

    private static CheckScope ReadScope(JsonElement value, List<Error> errors)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return CheckScope.All;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Malformed("$.scope must be an object with optional 'contentTypes' and 'fields'."));
            return CheckScope.All;
        }

        IReadOnlyList<string>? contentTypes = null;
        IReadOnlyList<string>? fields = null;
        foreach (var property in value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "contentTypes":
                    contentTypes = ReadStrings(property.Value, "$.scope.contentTypes", errors, allowNull: true);
                    break;
                case "fields":
                    fields = ReadStrings(property.Value, "$.scope.fields", errors, allowNull: true);
                    break;
                default:
                    errors.Add(Malformed($"$.scope.{property.Name} is not part of a scope; it has 'contentTypes' and 'fields'."));
                    break;
            }
        }

        return new CheckScope(contentTypes, fields);
    }

    private static CheckExample? ReadExample(JsonElement element, string path, List<Error> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Malformed($"{path} must be an object."));
            return null;
        }

        var count = errors.Count;
        string? name = null;
        var expected = CheckVerdict.Pass;
        JsonElement fields = default;
        IReadOnlyList<string>? sources = null;
        string? contentType = null;

        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value;
            switch (property.Name)
            {
                case "name":
                    name = ReadString(value, $"{path}.name", errors);
                    break;
                case "expect" when value.ValueKind == JsonValueKind.String && value.GetString() is "pass" or "fail":
                    expected = value.GetString() == "pass" ? CheckVerdict.Pass : CheckVerdict.Fail;
                    break;
                case "expect":
                    errors.Add(Malformed($"{path}.expect must be \"pass\" or \"fail\"."));
                    break;
                case "fields" when value.ValueKind == JsonValueKind.Object:
                    fields = value;
                    break;
                case "fields":
                    errors.Add(Malformed($"{path}.fields must be an object with one property per field."));
                    break;
                case "sources":
                    sources = ReadStrings(value, $"{path}.sources", errors, allowNull: false);
                    break;
                case "contentType" when value.ValueKind == JsonValueKind.Null:
                    break;
                case "contentType":
                    contentType = ReadString(value, $"{path}.contentType", errors);
                    break;
                default:
                    errors.Add(Malformed(
                        $"{path}.{property.Name} is not part of an example; it has 'name', 'expect', 'contentType', 'fields' and 'sources'."));
                    break;
            }
        }

        foreach (var required in new[] { "name", "expect", "fields" }.Where(property => !element.TryGetProperty(property, out _)))
        {
            errors.Add(Malformed($"{path}.{required} is missing."));
        }

        return errors.Count == count ? new CheckExample(name!, expected, fields, sources, contentType) : null;
    }

    private static string? ReadString(JsonElement value, string path, List<Error> errors)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        errors.Add(Malformed($"{path} must be a string."));
        return null;
    }

    private static List<string>? ReadStrings(JsonElement value, string path, List<Error> errors, bool allowNull)
    {
        if (allowNull && value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String))
        {
            return value.EnumerateArray().Select(item => item.GetString()!).ToList();
        }

        errors.Add(Malformed($"{path} must be an array of strings."));
        return null;
    }

    private static Error Malformed(string message)
    {
        return new Error(CheckErrorCodes.DefinitionMalformed, message);
    }
}
