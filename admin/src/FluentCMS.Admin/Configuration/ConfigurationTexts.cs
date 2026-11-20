using FluentCMS.Admin.Localization;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Configuration;

// The sentence of the error page for each problem, from the resources.
internal static class ConfigurationTexts
{
    public static string Describe(IStringLocalizer<AdminResources> texts, ConfigurationProblem problem) => problem.Kind switch
    {
        ConfigurationProblemKind.Unreadable => problem.StatusCode is { } status
            ? texts["Configuration.Error.UnreadableStatus", status]
            : texts["Configuration.Error.UnreadableTimeout"],
        ConfigurationProblemKind.NotJson => texts["Configuration.Error.NotJson"],
        ConfigurationProblemKind.NotAnObject => texts["Configuration.Error.NotAnObject"],
        ConfigurationProblemKind.Missing => texts["Configuration.Error.Missing", problem.Member!],
        ConfigurationProblemKind.Duplicate => texts["Configuration.Error.Duplicate", problem.Member!],
        _ => texts[RuleKey(problem.Rule), problem.Member!],
    };

    private static string RuleKey(ConfigurationRule rule) => rule switch
    {
        ConfigurationRule.HttpUrl or ConfigurationRule.UrlOrPath or ConfigurationRule.Origin or ConfigurationRule.HexColor
            or ConfigurationRule.Text or ConfigurationRule.ClientId or ConfigurationRule.ScopeList or ConfigurationRule.CultureName
            or ConfigurationRule.CultureList or ConfigurationRule.DefaultCultureOffered or ConfigurationRule.OriginList
            or ConfigurationRule.NotAdminOrigin or ConfigurationRule.Object => $"Configuration.Error.Rule{rule}",
        _ => "Configuration.Error.RuleInvalid",
    };
}
