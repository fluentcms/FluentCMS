using System.Text.Json;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Sites;

// The answers of /sites the page tells apart, by code and path only (spec 2.7).
internal static class SiteProblems
{
    public const string SiteNotFound = "FCMS2301";

    // A name of 1 to 128 characters, or a position below zero.
    public const string InvalidSite = "FCMS2304";
    public const string InvalidLocale = "FCMS2311";

    // A default locale the site has not enabled, or a fallback that is not another locale of the site, named once.
    public const string LocaleNotAllowed = "FCMS2312";
    public const string LocaleNotFound = "FCMS3135";

    // The default locale disabled or removed, or a locale removed that entries are in.
    public const string LocaleInUse = "FCMS3136";
    public const string LocaleExists = "FCMS3137";

    // A locale removed that other locales name in their fallbacks, one error for each at locales.{code}.fallbacks.
    public const string LocaleIsFallback = "FCMS3143";

    private const string NamingPrefix = "locales.";
    private const string NamingSuffix = ".fallbacks";

    // What a call may throw: a problem, or a server that did not answer or answered nonsense.
    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem }
        && (problem.Code == code || problem.Errors.Any(error => error.Code == code));

    public static bool Has(Exception exception, string code, string path) =>
        exception is ApiProblemException { Problem: var problem } && problem.Errors.Any(error => error.Code == code && error.Path == path);

    // The locales whose fallbacks name the one a removal kept, from the paths of its errors, in their order.
    public static IReadOnlyList<string> Naming(Exception exception) =>
        exception is ApiProblemException { Problem: var problem }
            ? [.. problem.Errors.Where(error => error.Code == LocaleIsFallback).Select(error => NamingCode(error.Path)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)]
            : [];

    private static string? NamingCode(string? path) =>
        path is not null && path.Length > NamingPrefix.Length + NamingSuffix.Length && path.StartsWith(NamingPrefix, StringComparison.Ordinal) && path.EndsWith(NamingSuffix, StringComparison.Ordinal)
            ? path[NamingPrefix.Length..^NamingSuffix.Length]
            : null;
}
