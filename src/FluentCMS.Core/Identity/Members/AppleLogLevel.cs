using AspNet.Security.OAuth.Apple;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Identity.Members;

// The Apple package writes the client secret it signs and the tokens of a sign-in at Trace, under the categories of its
// handler, its secret generator and its id token validator, all in AspNet.Security.OAuth.Apple. A level the host sets for
// every category, or for every category of one logging provider, does not reach them: the category stays at Information
// or above unless the host names it. A provider's own rules outrank a rule of every provider, however long its category,
// so a rule is added for each provider the host gives rules to as well. A rule only ever raises the level the category
// had, and keeps the filter of the rule it replaces.
internal sealed class AppleLogLevel : IPostConfigureOptions<LoggerFilterOptions>
{
    public const string Category = "AspNet.Security.OAuth.Apple";

    // What the package logs under; the startup report asks each whether it writes below Information.
    public static readonly string[] Categories =
    [
        typeof(AppleAuthenticationHandler).FullName!,
        Category + ".Internal.DefaultAppleClientSecretGenerator",
        Category + ".Internal.DefaultAppleIdTokenValidator",
    ];

    public void PostConfigure(string? name, LoggerFilterOptions options)
    {
        var rules = options.Rules.ToList();
        var named = rules.Where(rule => string.Equals(rule.CategoryName, Category, StringComparison.OrdinalIgnoreCase)).ToList();
        var everyProvider = named.LastOrDefault(rule => rule.ProviderName is null);

        if (everyProvider is null)
        {
            var current = Applying(rules, null);
            var level = current is null ? options.MinLevel : current.LogLevel ?? LogLevel.Trace;
            options.Rules.Add(new LoggerFilterRule(null, Category, Max(level, LogLevel.Information), current?.Filter));
        }

        var wanted = everyProvider?.LogLevel ?? LogLevel.Information;
        foreach (var provider in rules.Select(rule => rule.ProviderName).OfType<string>().Distinct(StringComparer.Ordinal)
            .Where(provider => !named.Any(rule => rule.ProviderName == provider)))
        {
            var current = Applying(rules, provider);
            options.Rules.Add(current is null
                ? new LoggerFilterRule(provider, Category, wanted, everyProvider?.Filter)
                : new LoggerFilterRule(provider, Category, Max(current.LogLevel ?? LogLevel.Trace, wanted), current.Filter));
        }
    }

    // The rule of one provider, or of every provider, that the logging library applies to the category: the longest
    // category that matches, the last of equals.
    private static LoggerFilterRule? Applying(List<LoggerFilterRule> rules, string? provider) =>
        rules.Where(rule => rule.ProviderName == provider && Matches(rule.CategoryName))
            .Select((rule, index) => (rule, index))
            .OrderBy(pair => pair.rule.CategoryName?.Length ?? 0)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.rule)
            .LastOrDefault();

    private static bool Matches(string? category)
    {
        if (category is null)
        {
            return true;
        }

        var wildcard = category.IndexOf('*', StringComparison.Ordinal);
        return wildcard < 0
            ? Category.StartsWith(category, StringComparison.OrdinalIgnoreCase)
            : Category.Length >= category.Length - 1
                && Category.StartsWith(category[..wildcard], StringComparison.OrdinalIgnoreCase)
                && Category.EndsWith(category[(wildcard + 1)..], StringComparison.OrdinalIgnoreCase);
    }

    private static LogLevel Max(LogLevel first, LogLevel second) => first > second ? first : second;
}
