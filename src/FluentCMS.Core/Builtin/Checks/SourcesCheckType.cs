using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.Checks;

// A change says where it came from (CHK-01, P3). On an entity every fact the change touches names its own source (SCH-03, ENT-02);
// on other content the change cites at least 'min' sources, one by default, when it touches a field in the check's scope.
internal sealed class SourcesCheckType : ICheckType
{
    public string Name => "sources";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        reader.PositiveInteger("min");
        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var subject = input.Subject;
        var changed = CheckContent.ChangedFields(subject);

        if (subject.Definition is { IsEntity: true })
        {
            var unsourced = CheckContent.Facts(subject)
                .Where(fact => changed.Contains(fact.Field, StringComparer.Ordinal))
                .Where(fact => !fact.Fact.TryGetProperty(FactMetadata.Source, out var source) || source.ValueKind != JsonValueKind.Object)
                .Select(fact => new CheckFinding(
                    $"Give the {fact.Field} fact a source that says where its value came from, such as a URL, a file, a person or a system.",
                    CheckContent.Append(string.Empty, fact.Field)))
                .ToList();

            return Task.FromResult(unsourced.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(unsourced));
        }

        var min = new CheckSettingsReader(Name, input.Settings).PositiveInteger("min") ?? 1;
        var cited = subject.Sources.Count(source => !string.IsNullOrWhiteSpace(source));
        if (changed.Count == 0 || cited >= min)
        {
            return Task.FromResult(CheckOutcome.Pass());
        }

        var fields = changed.Count <= 3 ? string.Join(", ", changed) : $"{string.Join(", ", changed.Take(3))} and {changed.Count - 3} more";
        var needed = min == 1 ? "a source" : $"at least {min} sources";
        return Task.FromResult(CheckOutcome.Fail(
            $"Cite {needed} for the change to {fields}: say where it came from, such as a URL, a file, an export or a person; it cites {cited}."));
    }
}
