using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Content;

internal enum PreviewMessageType
{
    Ready,
    Navigated,
}

// The version is the one the page's message named, which refresh to that page is posted in; hello is always version 2.
internal sealed record PreviewMessage(PreviewMessageType Type, string? Url, int Version);

// Version 2 of the preview's postMessage contract (spec 4.12): every message is a JSON object of the protocol
// fluentcms-preview, a version and a type. A message of another origin than the page loaded, of another protocol, of a higher
// version or of a type the admin does not take is ignored, and no message the admin sends carries a token. The admin reads
// versions 1 and 2; version 2 adds hello, which a page of version 1 ignores.
internal static class PreviewMessages
{
    public const string Protocol = "fluentcms-preview";

    public const int Version = 2;

    public static Uri? HttpUri(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;

    // The origin of an absolute http or https URL, as a browser serializes it in a message's origin: a host name in its
    // ASCII (punycode) form, so that an internationalized name matches; null for any other.
    public static string? OriginOf(string? url) => OriginOf(HttpUri(url));

    // A host that Uri takes and IDN refuses, such as one with a joiner, has no origin.
    public static string? OriginOf(Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        try
        {
            var host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : uri.IdnHost;
            return string.Create(CultureInfo.InvariantCulture, $"{uri.Scheme}://{host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}");
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    public static PreviewMessage? Read(string origin, string json, string expectedOrigin)
    {
        if (!string.Equals(origin, expectedOrigin, StringComparison.Ordinal))
        {
            return null;
        }

        JsonObject? message;
        try
        {
            message = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (message is null
            || Text(message, "protocol") != Protocol
            || message["version"] is not JsonValue version
            || !version.TryGetValue<int>(out var number)
            || number is < 1 or > Version)
        {
            return null;
        }

        return Text(message, "type") switch
        {
            "ready" => new PreviewMessage(PreviewMessageType.Ready, Text(message, "url"), number),
            "navigated" => new PreviewMessage(PreviewMessageType.Navigated, Text(message, "url"), number),
            _ => null,
        };
    }

    // The frame loaded a page: one that speaks version 2 answers with ready.
    public static string Hello() => new JsonObject
    {
        ["protocol"] = Protocol,
        ["version"] = Version,
        ["type"] = "hello",
    }.ToJsonString();

    // The draft was saved: the page reads it again. It is posted in the version the page's ready named.
    public static string Refresh(int speaks, string type, Guid id, string? locale, int version) => new JsonObject
    {
        ["protocol"] = Protocol,
        ["version"] = speaks,
        ["type"] = "refresh",
        ["entry"] = new JsonObject
        {
            ["type"] = type,
            ["id"] = id.ToString("D", CultureInfo.InvariantCulture),
            ["locale"] = locale,
            ["version"] = version,
        },
    }.ToJsonString();

    private static string? Text(JsonObject message, string name) =>
        message[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
