using System.Buffers;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Routing;

// One version of a routing rule, stored as one JSON document (ADR-0005):
// {"description":"...","when":{"itemKinds":["entity"],"contentTypes":["product"],"fields":["price"],"changeKinds":["update"],
//  "initiators":["agent"],"agents":["..."],"checks":"warn","riskScore":{"min":0,"max":40}},"then":{"disposition":"review","approvals":1}}
public sealed class RoutingRuleDefinition
{
    public const int MaxDescriptionLength = 1000;

    private static readonly JsonDocumentOptions ParseOptions = new() { AllowDuplicateProperties = false };

    private static readonly Dictionary<string, ItemKind> ItemKindNames = new(StringComparer.Ordinal)
    {
        ["entry"] = ItemKind.Entry,
        ["entity"] = ItemKind.Entity,
        ["media"] = ItemKind.Media,
        ["schema"] = ItemKind.Schema,
        ["rule"] = ItemKind.Rule,
    };

    private static readonly Dictionary<string, OperationKind> ChangeKindNames = new(StringComparer.Ordinal)
    {
        ["create"] = OperationKind.Create,
        ["update"] = OperationKind.Update,
        ["delete"] = OperationKind.Delete,
    };

    private static readonly Dictionary<string, InitiatorCondition> InitiatorNames = new(StringComparer.Ordinal)
    {
        ["person"] = InitiatorCondition.Person,
        ["agent"] = InitiatorCondition.Agent,
    };

    private static readonly Dictionary<string, RoutingCheckOutcome> CheckOutcomeNames = new(StringComparer.Ordinal)
    {
        ["pass"] = RoutingCheckOutcome.Pass,
        ["warn"] = RoutingCheckOutcome.Warn,
        ["fail"] = RoutingCheckOutcome.Fail,
    };

    public RoutingRuleDefinition(string description, RoutingConditions when, RoutingDisposition then)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(then);

        Description = description;
        When = when;
        Then = then;
    }

    // Why the rule exists, in the author's words; the routing decision quotes it as the reason (RTE-06).
    public string Description { get; }

    public RoutingConditions When { get; }

    public RoutingDisposition Then { get; }

    // Checks the shape of the document; Validate checks what its values mean.
    public static Result<RoutingRuleDefinition> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ParseOptions);
        }
        catch (JsonException exception)
        {
            return Result<RoutingRuleDefinition>.Failure(RoutingErrorCodes.DefinitionMalformed, $"The definition is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            var errors = new List<Error>();
            RoutingRuleDefinition? definition;
            try
            {
                definition = ReadDocument(document.RootElement, errors);
            }
            catch (InvalidOperationException)
            {
                // A string escapes half of a surrogate pair, which no .NET string can hold.
                return Result<RoutingRuleDefinition>.Failure(RoutingErrorCodes.DefinitionMalformed, "The definition holds text that is not valid Unicode.");
            }

            return errors.Count == 0 ? Result<RoutingRuleDefinition>.Success(definition!) : Result<RoutingRuleDefinition>.Failure(errors);
        }
    }

    // Read again from the text, so a duplicate property is refused here as it is in a stored document.
    public static Result<RoutingRuleDefinition> Parse(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Undefined
            ? Result<RoutingRuleDefinition>.Failure(RoutingErrorCodes.DefinitionMalformed, "$ must be an object with 'description', 'when' and 'then'.")
            : Parse(root.GetRawText());
    }

    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("description", Description);

            writer.WriteStartObject("when");
            WriteNames(writer, "itemKinds", When.ItemKinds?.Select(kind => Name(ItemKindNames, kind)));
            WriteNames(writer, "contentTypes", When.ContentTypes);
            WriteNames(writer, "fields", When.Fields);
            WriteNames(writer, "changeKinds", When.ChangeKinds?.Select(kind => Name(ChangeKindNames, kind)));
            WriteNames(writer, "initiators", When.Initiators?.Select(kind => Name(InitiatorNames, kind)));
            WriteNames(writer, "agents", When.Agents?.Select(id => id.ToString()));
            if (When.Checks is { } checks)
            {
                writer.WriteString("checks", Name(CheckOutcomeNames, checks));
            }

            if (When.MinRiskScore is not null || When.MaxRiskScore is not null)
            {
                writer.WriteStartObject("riskScore");
                if (When.MinRiskScore is { } min)
                {
                    writer.WriteNumber("min", min);
                }

                if (When.MaxRiskScore is { } max)
                {
                    writer.WriteNumber("max", max);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();

            writer.WriteStartObject("then");
            writer.WriteString("disposition", Then.Kind switch
            {
                DispositionKind.AutoPublish => "auto_publish",
                DispositionKind.Block => "block",
                _ => "review",
            });
            if (Then.Kind == DispositionKind.Review)
            {
                writer.WriteNumber("approvals", Then.Approvals);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // Every problem at once, each naming its condition, so the author fixes the rule in one round (UX-03).
    public Result Validate()
    {
        var errors = new List<Error>();

        if (string.IsNullOrWhiteSpace(Description) || Description.Length > MaxDescriptionLength)
        {
            errors.Add(new Error(
                RoutingErrorCodes.DescriptionInvalid,
                $"The description must say why the rule routes sets this way, in at most {MaxDescriptionLength} characters."));
        }

        errors.AddRange(EnumErrors("itemKinds", When.ItemKinds));
        errors.AddRange(ListErrors("contentTypes", When.ContentTypes, SchemaNames.IsSlug, "a content type slug such as 'product'"));
        errors.AddRange(ListErrors("fields", When.Fields, SchemaNames.IsFieldName, "a field name such as 'price'"));
        errors.AddRange(EnumErrors("changeKinds", When.ChangeKinds));
        errors.AddRange(EnumErrors("initiators", When.Initiators));
        errors.AddRange(ListErrors("agents", When.Agents?.Select(id => id.ToString()).ToList(), id => id != Guid.Empty.ToString(), "an agent credential id"));

        if (When.Checks is { } checks && !Enum.IsDefined(checks))
        {
            errors.Add(Condition($"The condition 'checks' holds '{checks}', which is not pass, warn or fail."));
        }

        if (When.MinRiskScore is { } min && min is < 0 or > RoutingConditions.HighestRiskScore
            || When.MaxRiskScore is { } max && max is < 0 or > RoutingConditions.HighestRiskScore)
        {
            errors.Add(Condition($"The risk score bounds are from 0 to {RoutingConditions.HighestRiskScore}."));
        }
        else if (When.MinRiskScore > When.MaxRiskScore)
        {
            errors.Add(Condition($"The risk score's min {When.MinRiskScore} is above its max {When.MaxRiskScore}."));
        }

        if (errors.Count == 0)
        {
            errors.AddRange(Parse(ToJson()).Errors);
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    private static IEnumerable<Error> EnumErrors<T>(string name, IReadOnlyList<T>? values)
        where T : struct, Enum
    {
        // An undefined value prints as its number.
        return ListErrors(name, values?.Select(value => value.ToString()).ToList(), value => !int.TryParse(value, out _), "a known value");
    }

    private static IEnumerable<Error> ListErrors(string name, IReadOnlyList<string>? values, Func<string?, bool> isValid, string expected)
    {
        if (values is null)
        {
            yield break;
        }

        if (values.Count == 0)
        {
            yield return Condition($"The condition '{name}' must not be empty; leave it out to match anything.");
            yield break;
        }

        foreach (var value in values.Where(value => !isValid(value)).Distinct())
        {
            yield return Condition($"The condition '{name}' holds '{value}', which is not {expected}.");
        }

        foreach (var value in values.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            yield return Condition($"The condition '{name}' holds '{value}' more than once.");
        }
    }

    private static RoutingRuleDefinition? ReadDocument(JsonElement root, List<Error> errors)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Malformed("$ must be an object with 'description', 'when' and 'then'."));
            return null;
        }

        var count = errors.Count;
        string? description = null;
        var when = RoutingConditions.Any;
        RoutingDisposition? then = null;

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "description" when property.Value.ValueKind == JsonValueKind.String:
                    description = property.Value.GetString();
                    break;
                case "description":
                    errors.Add(Malformed("$.description must be a string."));
                    break;
                case "when":
                    when = ReadConditions(property.Value, errors);
                    break;
                case "then":
                    then = ReadDisposition(property.Value, errors);
                    break;
                default:
                    errors.Add(Malformed($"$.{property.Name} is not part of a routing rule; it has 'description', 'when' and 'then'."));
                    break;
            }
        }

        foreach (var required in new[] { "description", "then" }.Where(name => !root.TryGetProperty(name, out _)))
        {
            errors.Add(Malformed($"$.{required} is missing."));
        }

        return errors.Count == count ? new RoutingRuleDefinition(description!, when, then!) : null;
    }

    private static RoutingConditions ReadConditions(JsonElement value, List<Error> errors)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return RoutingConditions.Any;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Malformed("$.when must be an object of conditions."));
            return RoutingConditions.Any;
        }

        var conditions = RoutingConditions.Any;
        foreach (var property in value.EnumerateObject())
        {
            var path = $"$.when.{property.Name}";
            switch (property.Name)
            {
                case "itemKinds":
                    conditions = conditions with { ItemKinds = ReadNames(property.Value, path, ItemKindNames, errors) };
                    break;
                case "contentTypes":
                    conditions = conditions with { ContentTypes = ReadStrings(property.Value, path, errors) };
                    break;
                case "fields":
                    conditions = conditions with { Fields = ReadStrings(property.Value, path, errors) };
                    break;
                case "changeKinds":
                    conditions = conditions with { ChangeKinds = ReadNames(property.Value, path, ChangeKindNames, errors) };
                    break;
                case "initiators":
                    conditions = conditions with { Initiators = ReadNames(property.Value, path, InitiatorNames, errors) };
                    break;
                case "agents":
                    conditions = conditions with { Agents = ReadAgents(property.Value, path, errors) };
                    break;
                case "checks" when property.Value.ValueKind == JsonValueKind.String
                    && CheckOutcomeNames.TryGetValue(property.Value.GetString()!, out var outcome):
                    conditions = conditions with { Checks = outcome };
                    break;
                case "checks":
                    errors.Add(Malformed($"{path} must be \"pass\", \"warn\" or \"fail\": the worst check outcome the set may have."));
                    break;
                case "riskScore":
                    conditions = ReadRiskScore(property.Value, path, conditions, errors);
                    break;
                default:
                    errors.Add(Malformed(
                        $"{path} is not a routing condition; they are 'itemKinds', 'contentTypes', 'fields', 'changeKinds', 'initiators', "
                        + "'agents', 'checks' and 'riskScore'."));
                    break;
            }
        }

        return conditions;
    }

    private static RoutingConditions ReadRiskScore(JsonElement value, string path, RoutingConditions conditions, List<Error> errors)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Malformed($"{path} must be an object with 'min', 'max' or both."));
            return conditions;
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is not ("min" or "max") || !property.Value.TryGetInt32(out var bound))
            {
                errors.Add(Malformed($"{path}.{property.Name} must be 'min' or 'max' with a whole number from 0 to {RoutingConditions.HighestRiskScore}."));
                continue;
            }

            conditions = property.Name == "min" ? conditions with { MinRiskScore = bound } : conditions with { MaxRiskScore = bound };
        }

        if (value.EnumerateObject().All(property => property.Name is not ("min" or "max")))
        {
            errors.Add(Malformed($"{path} must give 'min', 'max' or both."));
        }

        return conditions;
    }

    private static RoutingDisposition? ReadDisposition(JsonElement value, List<Error> errors)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Malformed("$.then must be an object with a 'disposition'."));
            return null;
        }

        string? disposition = null;
        int? approvals = null;
        foreach (var property in value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "disposition" when property.Value.ValueKind == JsonValueKind.String:
                    disposition = property.Value.GetString();
                    break;
                case "approvals" when property.Value.TryGetInt32(out var count):
                    approvals = count;
                    break;
                case "approvals":
                    errors.Add(Malformed("$.then.approvals must be a whole number."));
                    return null;
                default:
                    errors.Add(Malformed($"$.then.{property.Name} is not part of a disposition; it has 'disposition' and, for a review, 'approvals'."));
                    return null;
            }
        }

        switch (disposition)
        {
            case "review" when approvals is null or (>= 1 and <= RoutingDisposition.MaxApprovals):
                // A review needs one approval unless the rule says otherwise (RTE-05).
                return RoutingDisposition.Review(approvals ?? 1);
            case "review":
                errors.Add(new Error(
                    RoutingErrorCodes.DispositionInvalid,
                    $"A review needs from 1 to {RoutingDisposition.MaxApprovals} approvals, and the rule asks for {approvals}."));
                return null;
            case "auto_publish" or "block" when approvals is not null:
                errors.Add(new Error(RoutingErrorCodes.DispositionInvalid, $"Only a review takes 'approvals'; '{disposition}' does not."));
                return null;
            case "auto_publish":
                return RoutingDisposition.AutoPublish;
            case "block":
                return RoutingDisposition.Block;
            default:
                errors.Add(Malformed("$.then.disposition must be \"auto_publish\", \"review\" or \"block\"."));
                return null;
        }
    }

    private static List<T>? ReadNames<T>(JsonElement value, string path, Dictionary<string, T> names, List<Error> errors)
    {
        var strings = ReadStrings(value, path, errors);
        if (strings is null)
        {
            return null;
        }

        var unknown = strings.Where(name => !names.ContainsKey(name)).Distinct().ToList();
        if (unknown.Count > 0)
        {
            errors.Add(Malformed($"{path} holds {string.Join(", ", unknown.Select(name => $"'{name}'"))}; it takes {string.Join(", ", names.Keys)}."));
            return null;
        }

        return strings.Select(name => names[name]).ToList();
    }

    private static List<Guid>? ReadAgents(JsonElement value, string path, List<Error> errors)
    {
        var strings = ReadStrings(value, path, errors);
        if (strings is null)
        {
            return null;
        }

        var ids = new List<Guid>();
        foreach (var text in strings)
        {
            if (!Guid.TryParse(text, out var id))
            {
                errors.Add(Malformed($"{path} holds '{text}', which is not an agent credential id."));
                return null;
            }

            ids.Add(id);
        }

        return ids;
    }

    private static List<string>? ReadStrings(JsonElement value, string path, List<Error> errors)
    {
        if (value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String))
        {
            return value.EnumerateArray().Select(item => item.GetString()!).ToList();
        }

        errors.Add(Malformed($"{path} must be an array of strings."));
        return null;
    }

    private static void WriteNames(Utf8JsonWriter writer, string name, IEnumerable<string>? values)
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

    private static string Name<T>(Dictionary<string, T> names, T value)
        where T : struct, Enum
    {
        return names.First(pair => EqualityComparer<T>.Default.Equals(pair.Value, value)).Key;
    }

    private static Error Condition(string message)
    {
        return new Error(RoutingErrorCodes.ConditionInvalid, message);
    }

    private static Error Malformed(string message)
    {
        return new Error(RoutingErrorCodes.DefinitionMalformed, message);
    }
}
