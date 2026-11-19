using System.Globalization;
using FluentCMS.Content;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Plugins;

/// <summary>
/// The version of a record as an entity tag, as entries, settings and plugins have it: a read gives it as the <c>ETag</c>,
/// <c>"7"</c>, and a write that sends it back as <c>If-Match</c> is refused with a 412 when the record is at another version.
/// A proxy that compresses responses, such as nginx with gzip on, hands the client <c>W/"7"</c>, so a weak tag is read by its
/// value where RFC 9110 would refuse it; a list of tags or anything but a whole number is refused rather than read.
/// </summary>
public static class EntityVersions
{
    /// <summary>The header that carries the version a write was based on.</summary>
    public const string IfMatchHeader = "If-Match";

    internal const string IfMatchInvalid = "FCMS3131";

    /// <summary>
    /// The version a write was based on, from <c>If-Match</c>; <see langword="null"/> without the header or with <c>*</c>,
    /// where the write applies to whatever version is stored. A weak tag is read as a strong one.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The version, or <see langword="null"/>.</returns>
    /// <exception cref="ValidationException">The header holds more than one tag, or a tag that is not a version.</exception>
    public static int? IfMatch(HttpRequest request) => Read(request, allowAny: true);

    /// <summary>Gives a version as the <c>ETag</c> of a response.</summary>
    /// <param name="response">The response.</param>
    /// <param name="version">The version.</param>
    public static void SetETag(HttpResponse response, int version)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers.ETag = Tag(version);
    }

    internal static string Tag(int version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    internal static int? Read(HttpRequest request, bool allowAny)
    {
        ArgumentNullException.ThrowIfNull(request);

        var values = request.Headers.IfMatch;
        if (values.Count == 0)
        {
            return null;
        }

        var value = values.Count == 1 ? values[0]?.Trim() : null;
        if (value is not null && value.StartsWith("W/\"", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        if (value == "*")
        {
            return allowAny ? null : throw Invalid("The write needs the version it was based on, not *.");
        }

        if (value is null || value.Length < 3 || value[0] != '"' || value[^1] != '"'
            || !int.TryParse(value.AsSpan(1, value.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            throw Invalid($"{IfMatchHeader} takes one version as an entity tag, such as \"7\".");
        }

        return version;
    }

    private static ValidationException Invalid(string message) =>
        new([new ValidationError(IfMatchHeader, IfMatchInvalid, message)]);
}
