using FluentCMS.Content;
using FluentCMS.Plugins;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// If-Match as EntityVersions reads it, for the writes of core.
internal static class IfMatch
{
    public const string Header = EntityVersions.IfMatchHeader;

    // For a write that needs the version, such as a save.
    public static int Required(HttpRequest request) =>
        EntityVersions.Read(request, allowAny: false)
            ?? throw new ValidationException([new ValidationError(Header, PreconditionErrorCodes.IfMatchRequired, $"The write needs the version it was based on, as {Header}: \"<version>\".")]);

    // For a write that may name the version it reviewed; none, or *, is whatever the stored version is.
    public static int? Optional(HttpRequest request) => EntityVersions.Read(request, allowAny: true);

    // For a write whose ETag is a hash of what it holds rather than a version, such as the preferences: the tag without its
    // quotes, or null without the header or with *. A weak tag is read by its value, as EntityVersions reads a version.
    public static string? OptionalTag(HttpRequest request)
    {
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
            return null;
        }

        if (value is null || value.Length < 3 || value[0] != '"' || value[^1] != '"' || value[1..^1].Any(character => character is <= ' ' or '"' or > '~'))
        {
            throw new ValidationException([new ValidationError(Header, PreconditionErrorCodes.IfMatchInvalid, $"{Header} takes one entity tag, the ETag of a read.")]);
        }

        return value[1..^1];
    }

    public static string Tag(int version) => EntityVersions.Tag(version);

    public static void SetETag(HttpResponse response, int version) => EntityVersions.SetETag(response, version);
}
