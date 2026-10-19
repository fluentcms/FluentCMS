using System.Text.Encodings.Web;
using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Checks;

// How an LLM-judged check asks the model (CHK-01, ADR-0008). The check type's instructions are its own, written here; the content,
// and the settings an author or a rule wrote, go in data sections, since both are untrusted (SEC-03, RUL-07). The model answers in
// a fixed JSON shape, and an answer that does not fit, or no model at all, is needs review rather than a pass (MDL-03).
internal static class ModelJudgement
{
    // The profile LLM-judged checks run on, which an administrator points at a provider.
    public const string Profile = "checks";

    private const int MaxExcerptLength = 500;

    // An answer is one small object; a duplicate key could hide a second verdict behind the first.
    private static readonly JsonDocumentOptions AnswerOptions = new() { AllowDuplicateProperties = false, MaxDepth = 16 };

    // The model reads the content as written, not as \u escapes, which would hide non-Latin text and cost more tokens.
    private static readonly JsonSerializerOptions FieldsOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonElement AnswerSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "verdict": {"type": "string", "enum": ["pass", "fail"]},
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "pointer": {"type": "string"},
                  "excerpt": {"type": "string"},
                  "message": {"type": "string"}
                },
                "required": ["pointer", "excerpt", "message"],
                "additionalProperties": false
              }
            }
          },
          "required": ["verdict", "findings"],
          "additionalProperties": false
        }
        """);

    // Every judgement ends with the same contract for the answer, so each check type only says what to look for.
    private const string AnswerRules = """
        The "fields" section is the content as a JSON object with one property per field. Answer only with JSON in the given shape.
        Give "pass" with no findings when the content meets the test. Give "fail" only for passages that clearly do not, with one
        finding for each: "pointer" is the JSON Pointer of the field that holds it, such as /body or /tags/2; "excerpt" is the passage
        exactly as it appears; "message" tells the writer in one sentence what to change.
        """;

    public static async Task<CheckOutcome> JudgeAsync(
        CheckInput input,
        string checkType,
        string task,
        IReadOnlyList<ModelData> settings,
        CancellationToken cancellationToken)
    {
        if (input.Services.GetService<IModelGateway>() is not { } gateway || !gateway.IsAvailable(Profile))
        {
            return CheckOutcome.NeedsReview(
                $"No model is configured for LLM-judged checks, so a person reviews what the '{checkType}' check would have judged.");
        }

        var fields = input.Subject.Fields.ValueKind == JsonValueKind.Undefined ? "{}" : JsonSerializer.Serialize(input.Subject.Fields, FieldsOptions);
        var data = new List<ModelData> { new("fields", fields) };
        data.AddRange(settings);

        var asked = await gateway.AskAsync(
            new ModelRequest(Profile, $"check:{checkType}", task + "\n\n" + AnswerRules, data) { AnswerSchema = AnswerSchema },
            cancellationToken);
        if (!asked.IsSuccess)
        {
            return CheckOutcome.NeedsReview($"The model could not judge the '{checkType}' check: {asked.Errors[0].Message}");
        }

        var answer = asked.Value;
        var use = new CheckModelUse(answer.CallId, answer.Provider, answer.Model, answer.PromptHash, answer.Cost);
        return Read(answer.Text, input.Subject, checkType).WithModel(use);
    }

    private static CheckOutcome Read(string text, CheckSubject subject, string checkType)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(Unfenced(text), AnswerOptions);
            root = document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return Unreadable(checkType);
        }

        // Only the shape that was asked for counts, so an answer that says two things at once is read as neither.
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Any(property => property.Name is not ("verdict" or "findings"))
            || !root.TryGetProperty("verdict", out var verdict)
            || !root.TryGetProperty("findings", out var listed)
            || listed.ValueKind != JsonValueKind.Array)
        {
            return Unreadable(checkType);
        }

        switch (verdict.ValueKind == JsonValueKind.String ? verdict.GetString() : null)
        {
            case "pass" when listed.GetArrayLength() == 0:
                return CheckOutcome.Pass();
            case "fail":
                var findings = Findings(listed, subject).ToList();
                return findings.Count > 0
                    ? CheckOutcome.Fail(findings)
                    : CheckOutcome.NeedsReview($"The model failed the '{checkType}' check without saying what to change, so a person reviews it.");
            default:
                return Unreadable(checkType);
        }
    }

    // A pointer the content does not have, or an excerpt it does not hold, is dropped rather than shown, so a finding never
    // points at text the model made up.
    private static IEnumerable<CheckFinding> Findings(JsonElement findings, CheckSubject subject)
    {
        var strings = CheckContent.Strings(subject).ToList();
        foreach (var finding in findings.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
        {
            var message = Text(finding, "message");
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            var pointer = Text(finding, "pointer") ?? string.Empty;
            var excerpt = Text(finding, "excerpt");
            var place = strings.Where(item => item.Pointer == pointer || item.Pointer.StartsWith(pointer + "/", StringComparison.Ordinal)).ToList();

            yield return new CheckFinding(
                message.Trim(),
                place.Count > 0 ? pointer : string.Empty,
                excerpt is { Length: > 0 and <= MaxExcerptLength } && place.Any(item => item.Text.Contains(excerpt, StringComparison.Ordinal)) ? excerpt : null);
        }
    }

    // Models often wrap JSON in a Markdown code fence even when asked for JSON alone. Only a fence around the whole answer is taken
    // off; anything said after it leaves the answer as it is, which then does not read as JSON.
    private static string Unfenced(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('\n');
        var end = trimmed.Length - 3;
        return start > 0 && end > start ? trimmed[(start + 1)..end] : trimmed;
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static CheckOutcome Unreadable(string checkType)
    {
        return CheckOutcome.NeedsReview($"The model's answer to the '{checkType}' check could not be read, so a person reviews it.");
    }
}
