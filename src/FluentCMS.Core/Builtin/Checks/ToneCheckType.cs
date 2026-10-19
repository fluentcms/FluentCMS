using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Models;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// Content keeps the site's voice (CHK-01), such as "calm and factual, no hype, no exclamation marks". A model judges it.
internal sealed class ToneCheckType : ICheckType
{
    public const int MaxSettingLength = 1000;

    private const string Task = """
        You are a copy editor. Decide whether the content keeps the voice the "voice" section describes. Judge the wording and the
        manner, not whether the facts are true.
        """;

    public string Name => "tone";

    public CheckKind Kind => CheckKind.LlmJudged;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        if (reader.RequiredString("voice") is { Length: > MaxSettingLength })
        {
            reader.Invalid("voice", $"must be at most {MaxSettingLength} characters");
        }

        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var voice = new CheckSettingsReader(Name, input.Settings).RequiredString("voice") ?? string.Empty;
        return ModelJudgement.JudgeAsync(input, Name, Task, [new ModelData("voice", voice)], cancellationToken);
    }
}
