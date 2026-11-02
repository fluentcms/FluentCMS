using System.Globalization;
using System.Text.RegularExpressions;

namespace FluentCMS.Core.Localization;

// BCP 47 (RFC 5646) syntax, and the casing the RFC recommends: language in lower case, script in title case, region
// in upper case, everything else in lower case. Whether a tag is registered with IANA is not checked, because a site
// lists the locales it allows. Grandfathered tags are not accepted.
internal static partial class LocaleTag
{
    public const int MaxLength = 16;

    private const string Alpha = "[A-Za-z]";
    private const string AlphaNum = "[A-Za-z0-9]";

    // language [-extlang] [-script] [-region] *(-variant) *(-extension) [-privateuse], or privateuse alone.
    private const string Pattern =
        "^(?:"
        + $"(?<language>{Alpha}{{2,3}}|{Alpha}{{5,8}})(?<extlang>(?:-{Alpha}{{3}}){{0,3}})"
        + $"(?:-(?<script>{Alpha}{{4}}))?"
        + $"(?:-(?<region>{Alpha}{{2}}|[0-9]{{3}}))?"
        + $"(?<variants>(?:-(?:{AlphaNum}{{5,8}}|[0-9]{AlphaNum}{{3}}))*)"
        + $"(?<extensions>(?:-[0-9A-WY-Za-wy-z](?:-{AlphaNum}{{2,8}})+)*)"
        + $"(?<privateuse>-x(?:-{AlphaNum}{{1,8}})+)?"
        + $"|x(?:-{AlphaNum}{{1,8}})+"
        + ")$";

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    public static bool TryNormalize(string? tag, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > MaxLength || Tag().Match(tag) is not { Success: true } match)
        {
            return false;
        }

        if (!match.Groups["language"].Success)
        {
            normalized = tag.ToLowerInvariant();
            return true;
        }

        var parts = tag.Split('-');
        var scriptAt = match.Groups["script"].Success ? match.Groups["language"].Length + match.Groups["extlang"].Length + 1 : -1;
        var regionAt = match.Groups["region"].Success
            ? match.Groups["language"].Length + match.Groups["extlang"].Length + (match.Groups["script"].Success ? 5 : 0) + 1
            : -1;

        var result = new List<string>(parts.Length);
        var offset = 0;
        foreach (var part in parts)
        {
            // A part after a singleton is an extension subtag, lower case whatever its length.
            result.Add(offset == 0 ? part.ToLowerInvariant()
                : offset == scriptAt ? Title(part)
                : offset == regionAt ? part.ToUpperInvariant()
                : part.ToLowerInvariant());
            offset += part.Length + 1;
        }

        normalized = string.Join('-', result);
        return true;
    }

    private static string Title(string part) => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();

    // fr-CA-x-a gives fr-CA, and then fr; empty when no subtag is left to remove.
    public static string? Parent(string tag)
    {
        var parts = tag.Split('-').ToList();
        while (parts.Count > 1)
        {
            parts.RemoveAt(parts.Count - 1);

            // A singleton cannot end a tag, so it goes with what it introduced.
            if (parts[^1].Length == 1)
            {
                continue;
            }

            return string.Join('-', parts);
        }

        return null;
    }
}
