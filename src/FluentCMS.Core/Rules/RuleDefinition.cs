using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Rules;

// What a rule is about (RUL-02). Permission and routing rules are written here and evaluated by routing (F-RTE).
public enum RuleKind
{
    VoiceAndStyle,
    Factual,
    Structural,
    PermissionAndRouting,
}

// Which content a rule speaks to: content types by slug, fields by name and locales by tag, each null for all of them (RUL-03).
// Nothing reads the locales in M1 (I18-02); the slot is kept so a rule written now keeps its meaning later.
public sealed record RuleScope(IReadOnlyList<string>? ContentTypes = null, IReadOnlyList<string>? Fields = null, IReadOnlyList<string>? Locales = null)
{
    public static RuleScope All { get; } = new();
}

// One version of a rule, stored as one JSON document (ADR-0005):
// {"kind":"voice_and_style","text":"Never claim best in class.","severity":"block","scope":{"contentTypes":["product-page"]}}
public sealed partial class RuleDefinition
{
    public const int MaxTextLength = 4000;
    public const int MaxScopeItems = 100;

    private static readonly JsonDocumentOptions ParseOptions = new() { AllowDuplicateProperties = false };

    public RuleDefinition(RuleKind kind, string text, CheckSeverity severity = CheckSeverity.Block, RuleScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        Kind = kind;
        Text = text;
        Severity = severity;
        Scope = scope ?? RuleScope.All;
    }

    public RuleKind Kind { get; }

    // The rule in the author's words, which agents read and checks are proposed from. It came from a person, so a model reads it as
    // data, never as instructions (RUL-07).
    public string Text { get; }

    public CheckSeverity Severity { get; }

    public RuleScope Scope { get; }

    // What makes a definition one a rule can hold, each problem with what to change.
    public IReadOnlyList<Error> Validate()
    {
        var errors = new List<Error>();

        if (!Enum.IsDefined(Kind))
        {
            errors.Add(new Error(RuleErrorCodes.KindUnknown, $"The kind must be one of {string.Join(", ", Enum.GetValues<RuleKind>().Select(KindName))}."));
        }

        if (string.IsNullOrWhiteSpace(Text) || Text.Length > MaxTextLength)
        {
            errors.Add(new Error(RuleErrorCodes.TextInvalid, $"Write the rule as text of at most {MaxTextLength} characters, such as \"Never claim best in class.\""));
        }

        if (!Enum.IsDefined(Severity))
        {
            errors.Add(new Error(RuleErrorCodes.SeverityUnknown, "The severity must be block or warn."));
        }

        Check(errors, "contentTypes", Scope.ContentTypes, SchemaNames.IsSlug, "a content type's slug, such as product-page");
        Check(errors, "fields", Scope.Fields, SchemaNames.IsFieldName, "a field's name, such as headline");
        Check(errors, "locales", Scope.Locales, value => value is not null && LocalePattern().IsMatch(value), "a language tag, such as en or de-CH");

        return errors;
    }

    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", KindName(Kind));
            writer.WriteString("text", Text);
            writer.WriteString("severity", Severity == CheckSeverity.Warn ? "warn" : "block");
            if (Scope.ContentTypes is not null || Scope.Fields is not null || Scope.Locales is not null)
            {
                writer.WriteStartObject("scope");
                WriteList(writer, "contentTypes", Scope.ContentTypes);
                WriteList(writer, "fields", Scope.Fields);
                WriteList(writer, "locales", Scope.Locales);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // Reads a stored version back. Only the shape is checked here; Validate says what a definition may hold.
    public static Result<RuleDefinition> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json, ParseOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("severity", out var severity) || severity.ValueKind != JsonValueKind.String)
            {
                return Malformed("it needs kind, text and severity as strings");
            }

            if (KindOf(kind.GetString()!) is not { } parsedKind)
            {
                return Malformed($"the kind '{kind.GetString()}' is not one this version knows");
            }

            var scope = RuleScope.All;
            if (root.TryGetProperty("scope", out var stored))
            {
                if (stored.ValueKind != JsonValueKind.Object)
                {
                    return Malformed("the scope is not an object");
                }

                scope = new RuleScope(ReadList(stored, "contentTypes"), ReadList(stored, "fields"), ReadList(stored, "locales"));
            }

            CheckSeverity parsedSeverity;
            switch (severity.GetString())
            {
                case "block":
                    parsedSeverity = CheckSeverity.Block;
                    break;
                case "warn":
                    parsedSeverity = CheckSeverity.Warn;
                    break;
                default:
                    return Malformed($"the severity '{severity.GetString()}' is not block or warn");
            }

            return Result<RuleDefinition>.Success(new RuleDefinition(parsedKind, text.GetString()!, parsedSeverity, scope));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Malformed(exception.Message);
        }
    }

    // As stored and as tools show it: snake_case, such as voice_and_style.
    public static string KindName(RuleKind kind)
    {
        return kind switch
        {
            RuleKind.VoiceAndStyle => "voice_and_style",
            RuleKind.Factual => "factual",
            RuleKind.Structural => "structural",
            RuleKind.PermissionAndRouting => "permission_and_routing",
            _ => kind.ToString(),
        };
    }

    public static RuleKind? KindOf(string name)
    {
        return Enum.GetValues<RuleKind>().Cast<RuleKind?>().FirstOrDefault(kind => KindName(kind!.Value) == name);
    }

    private static void Check(List<Error> errors, string name, IReadOnlyList<string>? values, Func<string?, bool> valid, string example)
    {
        if (values is null)
        {
            return;
        }

        if (values.Count is 0 or > MaxScopeItems)
        {
            errors.Add(new Error(RuleErrorCodes.ScopeInvalid, $"The scope's {name} lists from 1 to {MaxScopeItems} values, or is left out for all of them."));
            return;
        }

        foreach (var value in values.Where(value => !valid(value)).Distinct())
        {
            errors.Add(new Error(RuleErrorCodes.ScopeInvalid, $"'{value}' in the scope's {name} is not {example}."));
        }

        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            errors.Add(new Error(RuleErrorCodes.ScopeInvalid, $"The scope's {name} names a value more than once."));
        }
    }

    private static void WriteList(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
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

    private static IReadOnlyList<string>? ReadList(JsonElement scope, string name)
    {
        if (!scope.TryGetProperty(name, out var values))
        {
            return null;
        }

        return values.ValueKind == JsonValueKind.Array && values.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String)
            ? [.. values.EnumerateArray().Select(value => value.GetString()!)]
            : throw new InvalidOperationException($"The scope's {name} is not a list of strings.");
    }

    private static Result<RuleDefinition> Malformed(string reason)
    {
        return Result<RuleDefinition>.Failure(RuleErrorCodes.DefinitionMalformed, $"The stored rule cannot be read: {reason}.");
    }

    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalePattern();
}
