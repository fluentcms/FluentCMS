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

    public static string Tag(int version) => EntityVersions.Tag(version);

    public static void SetETag(HttpResponse response, int version) => EntityVersions.SetETag(response, version);
}
