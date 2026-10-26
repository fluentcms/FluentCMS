using System.Text.Encodings.Web;
using System.Text.Json;
using FluentCMS.Core.Builtin.Checks;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Models;

namespace FluentCMS.Core.Rules;

// Proposes checks that enforce a rule (RUL-04), asking the model on the 'rules' profile. The instructions are written here; the rule's
// text and scope go in data sections, since a person wrote them (SEC-03, RUL-07). A proposal is kept only if it validates against the
// installed check types. Without a model, or without a usable answer, the rule is advisory rather than in error (MDL-03).
public sealed class RuleCompiler(IModelGateway gateway, CheckTypeRegistry checkTypes)
{
    public const string Profile = "rules";
    public const string JobType = "rules.compile";
    public const int MaxProposals = 3;

    public const string NoModel =
        "No model is configured to propose checks, so the rule is advisory: agents read it as guidance and it blocks nothing. "
        + "Configure the 'rules' model profile, then propose checks again.";

    private const string Unreadable = "The model's answer could not be read, so the rule is advisory until checks are proposed again.";

    // An answer is one small object; a duplicate key could hide a second proposal behind the first.
    private static readonly JsonDocumentOptions AnswerOptions = new() { AllowDuplicateProperties = false, MaxDepth = 32 };

    // The model reads the scope as written, not as \u escapes.
    private static readonly JsonSerializerOptions ScopeOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // The built-in types a rule can compile to, with what each does and the settings it takes, in the words the model reads.
    private static readonly (string Type, string Use)[] Catalog =
    [
        ("terms", """fails content that uses any of the listed words or phrases, matched as whole words ignoring case. Settings: {"terms": ["world-class", "revolutionary"]}."""),
        ("pattern", """fails text a regular expression matches, or with "mode": "require" text it does not match. Settings: {"pattern": "[0-9]+ ?%", "mode": "forbid", "ignoreCase": true}; no backreferences, lookarounds or atomic groups."""),
        ("length", """fails text shorter than min or longer than max, in characters or words. Settings: {"max": 60, "unit": "characters"}, with min, max or both."""),
        ("range", """fails a number below min or above max. Settings: {"min": 0, "max": 10000}, with min, max or both."""),
        ("tone", """a model judges whether content keeps a voice. Settings: {"voice": "calm and factual, no hype"}."""),
        ("claims", """a model judges whether factual, comparative, statistical, health, safety and legal claims are supported by a cited source or an attribution. Settings: {"focus": "price comparisons"}, where focus is optional."""),
        ("readingLevel", """a model judges whether content reads at a level. Settings: {"level": "plain English a twelve-year-old can follow"}."""),
    ];

    private static readonly JsonElement AnswerSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "checks": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "type": {"type": "string"},
                  "description": {"type": "string"},
                  "settings": {"type": "string"},
                  "examples": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "name": {"type": "string"},
                        "expect": {"type": "string", "enum": ["pass", "fail"]},
                        "contentType": {"type": "string"},
                        "fields": {"type": "string"}
                      },
                      "required": ["name", "expect", "contentType", "fields"],
                      "additionalProperties": false
                    }
                  }
                },
                "required": ["type", "description", "settings", "examples"],
                "additionalProperties": false
              }
            }
          },
          "required": ["checks"],
          "additionalProperties": false
        }
        """);

    public bool IsAvailable => gateway.IsAvailable(Profile);

    // onBehalfOf is the rule's author, whose work the check engine asks the model for.
    public async Task<RuleCompileOutcome> ProposeAsync(RuleDefinition rule, Initiator? onBehalfOf, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (!IsAvailable)
        {
            return RuleCompileOutcome.Advisory(NoModel);
        }

        var offered = Catalog.Where(entry => checkTypes.Find(entry.Type) is not null).ToList();
        var scope = JsonSerializer.Serialize(
            new { kind = RuleDefinition.KindName(rule.Kind), contentTypes = rule.Scope.ContentTypes, fields = rule.Scope.Fields },
            ScopeOptions);

        var asked = await gateway.AskAsync(
            new ModelRequest(Profile, "rule:compile", Instructions(offered), [new ModelData("rule", rule.Text), new ModelData("scope", scope)])
            {
                AnswerSchema = AnswerSchema,
                OnBehalfOf = onBehalfOf,
            },
            cancellationToken);
        if (!asked.IsSuccess)
        {
            return RuleCompileOutcome.Advisory(
                $"The model could not propose checks, so the rule is advisory until checks are proposed again: {asked.Errors[0].Message}");
        }

        return Read(asked.Value.Text, rule, offered.Select(entry => entry.Type).ToHashSet(StringComparer.Ordinal)) with
        {
            Model = asked.Value.Model,
            PromptHash = asked.Value.PromptHash,
        };
    }

    private static string Instructions(IReadOnlyList<(string Type, string Use)> offered)
    {
        var types = string.Join("\n", offered.Select(entry => $"- {entry.Type}: {entry.Use}"));
        return $"""
            You turn a content rule a team wrote into automated checks that enforce it. The "rule" section is the rule in its author's
            words. The "scope" section is JSON: the rule's kind, and the content types and fields it applies to, each null for all.

            Propose at most {MaxProposals} checks that together enforce the rule, each of one of these types:
            {types}

            Prefer a type that decides without a model (terms, pattern, length, range) when it enforces the rule exactly, and a
            model-judged type only when none can. Propose no check when no type can enforce the rule, such as a rule about who may
            publish; the rule is then guidance for writers.

            Give each check: "type"; "description", one sentence saying what it enforces; "settings", its settings as a JSON object
            written as a string; and "examples", at least one piece of content the check must pass and one it must fail. Give each
            example: "name", a few words saying what it shows; "expect", "pass" or "fail"; "contentType", one of the scope's content
            types, or "" when the scope names none; and "fields", the content as a JSON object with one property per field, written as
            a string, using the scope's fields when it names some. Answer only with JSON in the given shape.
            """;
    }

    // Only the types the prompt offered count, so an answer cannot reach a type the compile does not describe.
    private RuleCompileOutcome Read(string text, RuleDefinition rule, HashSet<string> offered)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(ModelJudgement.Unfenced(text), AnswerOptions);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return RuleCompileOutcome.Advisory(Unreadable);
        }

        // Only the shape that was asked for counts.
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Any(property => property.Name != "checks")
            || !root.TryGetProperty("checks", out var listed)
            || listed.ValueKind != JsonValueKind.Array)
        {
            return RuleCompileOutcome.Advisory(Unreadable);
        }

        var proposals = new List<CheckDefinition>();
        var unusable = 0;
        foreach (var item in listed.EnumerateArray().Take(MaxProposals))
        {
            if (Proposal(item, rule) is { } proposal && offered.Contains(proposal.Type) && proposal.Validate(checkTypes).IsSuccess)
            {
                proposals.Add(proposal);
            }
            else
            {
                unusable++;
            }
        }

        if (proposals.Count > 0)
        {
            return new RuleCompileOutcome(
                RuleCompileStatus.Proposed,
                unusable == 0 ? null : $"{Checks(unusable)} more that the model proposed could not be used and {(unusable == 1 ? "was" : "were")} left out.",
                proposals);
        }

        return RuleCompileOutcome.Advisory(unusable == 0
            ? "The model found no check that can enforce this rule, so it is advisory: agents read it as guidance and it blocks nothing."
            : $"The model proposed {Checks(unusable)} that could not be used, so the rule is advisory until checks are proposed again.");
    }

    // A proposal takes the rule's severity and scope, whatever the model says; an example that names no content type is of the
    // first type the scope names.
    private static CheckDefinition? Proposal(JsonElement item, RuleDefinition rule)
    {
        if (item.ValueKind != JsonValueKind.Object
            || item.EnumerateObject().Any(property => property.Name is not ("type" or "description" or "settings" or "examples"))
            || Text(item, "type") is not { } type
            || Text(item, "description") is not { } description
            || ObjectIn(Text(item, "settings")) is not { } settings
            || !item.TryGetProperty("examples", out var listed)
            || listed.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var examples = new List<CheckExample>();
        foreach (var example in listed.EnumerateArray())
        {
            if (example.ValueKind != JsonValueKind.Object
                || Text(example, "name") is not { } name
                || Text(example, "expect") is not ("pass" or "fail")
                || ObjectIn(Text(example, "fields")) is not { } fields)
            {
                return null;
            }

            var contentType = Text(example, "contentType") is { Length: > 0 } named ? named : rule.Scope.ContentTypes?[0];
            var expected = Text(example, "expect") == "pass" ? CheckVerdict.Pass : CheckVerdict.Fail;
            examples.Add(new CheckExample(name, expected, fields, contentType: contentType));
        }

        return new CheckDefinition(type, description, settings, examples, rule.Severity, new CheckScope(rule.Scope.ContentTypes, rule.Scope.Fields));
    }

    // A string that escapes half of a surrogate pair holds no .NET string, so it reads as missing.
    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // Settings and fields come as JSON written in a string, which a provider's strict answer schema can carry.
    private static JsonElement? ObjectIn(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, AnswerOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Checks(int count)
    {
        return count == 1 ? "1 check" : $"{count} checks";
    }
}
