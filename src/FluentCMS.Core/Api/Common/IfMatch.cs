using System.Globalization;
using FluentCMS.Content;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// The version a write was based on travels as an entity tag, If-Match: "7", and a response that holds a version gives it
// back as the ETag. The tag is strong, but a proxy that compresses responses, such as nginx with gzip on, hands the client
// W/"7", so a weak tag is read by its opaque value where RFC 9110 would refuse it. A list of tags or anything but a whole
// number is refused rather than read.
internal static class IfMatch
{
    public const string Header = "If-Match";

    // For a write that needs the version, such as a save.
    public static int Required(HttpRequest request) =>
        Read(request, allowAny: false)
            ?? throw new ValidationException([new ValidationError(Header, PreconditionErrorCodes.IfMatchRequired, $"The write needs the version it was based on, as {Header}: \"<version>\".")]);

    // For a write that may name the version it reviewed; none, or *, is whatever the stored version is.
    public static int? Optional(HttpRequest request) => Read(request, allowAny: true);

    public static string Tag(int version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    public static void SetETag(HttpResponse response, int version)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers.ETag = Tag(version);
    }

    private static int? Read(HttpRequest request, bool allowAny)
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
            throw Invalid($"{Header} takes one version as an entity tag, such as \"7\".");
        }

        return version;
    }

    private static ValidationException Invalid(string message) =>
        new([new ValidationError(Header, PreconditionErrorCodes.IfMatchInvalid, message)]);
}
