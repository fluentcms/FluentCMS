using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Models;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// Content reads at the level the site sets (CHK-01), such as "plain English a twelve-year-old can follow" or "CEFR B1". A model
// judges it.
internal sealed class ReadingLevelCheckType : ICheckType
{
    private const string Task = """
        You are an editor for plain language. Decide whether a reader at the level the "level" section describes can read the
        content. Fail passages that are harder than that level, such as long sentences, jargon or unexplained terms.
        """;

    public string Name => "readingLevel";

    public CheckKind Kind => CheckKind.LlmJudged;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        if (reader.RequiredString("level") is { Length: > ToneCheckType.MaxSettingLength })
        {
            reader.Invalid("level", $"must be at most {ToneCheckType.MaxSettingLength} characters");
        }

        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var level = new CheckSettingsReader(Name, input.Settings).RequiredString("level") ?? string.Empty;
        return ModelJudgement.JudgeAsync(input, Name, Task, [new ModelData("level", level)], cancellationToken);
    }
}
