using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// A regular expression text must not match, or, with mode "require", one every text must match (CHK-01). Patterns come from rule
// authors and agents, so they run on the non-backtracking engine, whose time grows only with the length of the text.
internal sealed class PatternCheckType : ICheckType
{
    public const int MaxPatternLength = 500;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public string Name => "pattern";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        var pattern = reader.RequiredString("pattern");
        var ignoreCase = reader.Flag("ignoreCase");
        var mode = reader.OptionalString("mode");

        if (mode is not (null or "forbid" or "require"))
        {
            reader.Invalid("mode", "must be \"forbid\" or \"require\"");
        }

        if (pattern is { Length: > MaxPatternLength })
        {
            reader.Invalid("pattern", $"must be at most {MaxPatternLength} characters");
        }
        else if (pattern is not null && Compile(pattern, ignoreCase) is { Error: { } problem })
        {
            reader.Invalid("pattern", $"must be a regular expression without backreferences, lookarounds or atomic groups ({problem})");
        }

        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var reader = new CheckSettingsReader(Name, input.Settings);
        var source = reader.RequiredString("pattern") ?? string.Empty;
        var regex = Compile(source, reader.Flag("ignoreCase")).Regex!;
        var require = reader.OptionalString("mode") == "require";

        var findings = new List<CheckFinding>();
        foreach (var (pointer, text) in CheckContent.Strings(input.Subject))
        {
            var field = CheckContent.FieldOf(pointer);
            if (require)
            {
                if (!regex.IsMatch(text))
                {
                    findings.Add(new CheckFinding($"Change {field} so that it matches {source}.", pointer, CheckContent.Quote(text)));
                }

                continue;
            }

            findings.AddRange(regex.Matches(text).Where(match => match.Length > 0).Select(match => new CheckFinding(
                $"Remove or rewrite '{match.Value}' in {field}; this check does not allow text that matches {source}.",
                pointer,
                match.Value)));
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    private static (Regex? Regex, string? Error) Compile(string pattern, bool ignoreCase)
    {
        var options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        try
        {
            return (new Regex(pattern, options, MatchTimeout), null);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return (null, exception.Message);
        }
    }
}
