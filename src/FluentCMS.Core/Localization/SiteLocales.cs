using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Localization;

namespace FluentCMS.Core.Localization;

// The locales one site allows. Pure, so the chain rules are testable without a database.
internal sealed class SiteLocales
{
    private readonly Dictionary<string, SiteLocale> _enabled;

    public SiteLocales(string defaultLocale, IEnumerable<SiteLocale> locales)
    {
        Default = defaultLocale;
        _enabled = locales.Where(locale => locale.Enabled).ToDictionary(locale => locale.Code, StringComparer.OrdinalIgnoreCase);
    }

    public string Default { get; }

    // In the order the site sets.
    public IReadOnlyList<string> Codes => [.. _enabled.Values.OrderBy(locale => locale.Position).ThenBy(locale => locale.Code, StringComparer.Ordinal).Select(locale => locale.Code)];

    // The code as the site lists it, for a tag that is well formed and allowed. Throws a problem code otherwise.
    public string Validate(string tag)
    {
        if (!LocaleTag.TryNormalize(tag, out var normalized))
        {
            throw Problem(LocaleErrorCodes.InvalidLocale, $"'{tag}' is not a well-formed BCP 47 language tag.");
        }

        return _enabled.TryGetValue(normalized, out var locale)
            ? locale.Code
            : throw Problem(LocaleErrorCodes.LocaleNotAllowed, $"The site does not allow the locale '{normalized}'.");
    }

    // The requested locale, then what the site's row for it lists. A row with no list gets the implicit chain: the tag
    // with its last subtag removed for as long as one is left, then the site's default. A code in the list that the
    // site no longer allows is skipped.
    public FallbackChain Chain(string locale)
    {
        List<string> codes = [locale];
        if (_enabled.TryGetValue(locale, out var row) && Listed(row) is { } listed)
        {
            codes.AddRange(listed.Where(_enabled.ContainsKey));
        }
        else
        {
            for (var parent = LocaleTag.Parent(locale); parent is not null; parent = LocaleTag.Parent(parent))
            {
                if (_enabled.ContainsKey(parent))
                {
                    codes.Add(parent);
                }
            }

            if (_enabled.ContainsKey(Default))
            {
                codes.Add(Default);
            }
        }

        return new FallbackChain(codes.Select(code => _enabled.TryGetValue(code, out var found) ? found.Code : code));
    }

    private static List<string>? Listed(SiteLocale row)
    {
        if (row.Fallbacks is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(row.Fallbacks);
        }
        catch (JsonException)
        {
            // Treated as no list: a bad chain must not take the site's reads down.
            return null;
        }
    }

    private static ValidationException Problem(string code, string message) => new([new ValidationError("locale", code, message)]);
}
