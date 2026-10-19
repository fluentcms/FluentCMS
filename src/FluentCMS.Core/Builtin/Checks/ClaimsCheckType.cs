using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Models;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// Claims that need support have it (CHK-01, P3): factual, comparative, statistical, health, safety and legal claims, and those
// 'focus' names. A claim the content attributes, or one a source the change cites covers, is supported. A model judges it.
internal sealed class ClaimsCheckType : ICheckType
{
    private const string Task = """
        You are a fact-checking editor. Find the claims in the content that need support: factual, comparative, statistical, health,
        safety and legal claims, superlatives such as "the best" or "the fastest", and any kind the "focus" section names. A claim is
        supported when the content attributes it, or when the "sources" section lists a source that plausibly covers it. Fail each
        claim that is not supported, and say whether to cite a source, attribute it or remove it. Do not judge whether a claim is
        true.
        """;

    public string Name => "claims";

    public CheckKind Kind => CheckKind.LlmJudged;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        if (reader.OptionalString("focus") is { Length: > ToneCheckType.MaxSettingLength })
        {
            reader.Invalid("focus", $"must be at most {ToneCheckType.MaxSettingLength} characters");
        }

        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var focus = new CheckSettingsReader(Name, input.Settings).OptionalString("focus") ?? "No other kinds.";
        var sources = input.Subject.Sources.Where(source => !string.IsNullOrWhiteSpace(source)).ToList();

        return ModelJudgement.JudgeAsync(
            input,
            Name,
            Task,
            [new ModelData("focus", focus), new ModelData("sources", sources.Count == 0 ? "The change cites no sources." : string.Join("\n", sources))],
            cancellationToken);
    }
}
