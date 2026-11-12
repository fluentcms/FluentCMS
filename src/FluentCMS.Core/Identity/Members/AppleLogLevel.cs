using AspNet.Security.OAuth.Apple;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Identity.Members;

// The Apple package writes the client secret it signs and the tokens of a sign-in at Trace, under the categories of its
// handler, its secret generator and its id token validator, all in AspNet.Security.OAuth.Apple. A level the host sets for
// every category, or for every category of one logging provider, does not reach them: the category stays at Information
// unless the host names it. A provider's own default outranks a rule of every provider, however long its category, so a
// rule is added for each provider the host gives rules to, at the level the host named for every provider if it did.
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
        var named = options.Rules.Where(rule => string.Equals(rule.CategoryName, Category, StringComparison.OrdinalIgnoreCase)).ToList();
        var everyProvider = named.LastOrDefault(rule => rule.ProviderName is null);
        var providers = options.Rules.Select(rule => rule.ProviderName).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

        if (everyProvider is null)
        {
            options.Rules.Add(new LoggerFilterRule(null, Category, LogLevel.Information, null));
        }

        foreach (var provider in providers.Where(provider => !named.Any(rule => rule.ProviderName == provider)))
        {
            options.Rules.Add(new LoggerFilterRule(provider, Category, everyProvider?.LogLevel ?? LogLevel.Information, everyProvider?.Filter));
        }
    }
}
