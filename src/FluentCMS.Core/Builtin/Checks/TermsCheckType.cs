using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// Words and phrases content must not use, such as "best in class" (CHK-01). A phrase matches whole words only, ignoring case unless
// the check says otherwise, and its words match across any spaces or hyphens, so "best-in-class" is found too.
internal sealed class TermsCheckType : ICheckType
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public string Name => "terms";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        var terms = reader.RequiredStringList("terms");
        reader.Flag("caseSensitive");

        if (terms is not null && terms.Any(term => Words(term).Length == 0))
        {
            reader.Invalid("terms", "must hold terms with at least one letter or digit each");
        }

        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var reader = new CheckSettingsReader(Name, input.Settings);
        var terms = reader.RequiredStringList("terms") ?? [];
        var options = RegexOptions.CultureInvariant | (reader.Flag("caseSensitive") ? RegexOptions.None : RegexOptions.IgnoreCase);
        var patterns = terms.Where(term => Words(term).Length > 0).Select(term => (Term: term, Pattern: Pattern(term, options))).ToList();

        var findings = new List<CheckFinding>();
        foreach (var (pointer, text) in CheckContent.Strings(input.Subject.Fields))
        {
            foreach (var (term, pattern) in patterns)
            {
                findings.AddRange(pattern.Matches(text).Select(match => new CheckFinding(
                    $"Remove or rewrite '{match.Value}' in {CheckContent.FieldOf(pointer)}; '{term}' is on this check's list of terms not to use.",
                    pointer,
                    match.Value)));
            }
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    // The words are literal text, so the pattern cannot backtrack badly; the timeout guards the content's length.
    private static Regex Pattern(string term, RegexOptions options)
    {
        var words = string.Join(@"[\s\-‐-―]+", Words(term).Select(Regex.Escape));
        return new Regex($@"(?<![\p{{L}}\p{{N}}]){words}(?![\p{{L}}\p{{N}}])", options, MatchTimeout);
    }

    private static string[] Words(string term)
    {
        return Regex.Split(term, @"[\s\-‐-―]+").Where(word => word.Any(char.IsLetterOrDigit)).ToArray();
    }
}
