using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Content;

internal enum PreviewMessageType
{
    Ready,
    Navigated,
}

internal sealed record PreviewMessage(PreviewMessageType Type, string? Url);

// Version 1 of the preview's postMessage contract (spec 4.12): every message is a JSON object of the protocol
// fluentcms-preview, a version and a type. A message of another origin than the page loaded, of another protocol, of a higher
// version or of a type the admin does not take is ignored, and no message the admin sends carries a token.
internal static class PreviewMessages
{
    public const string Protocol = "fluentcms-preview";

    public const int Version = 1;

    public static Uri? HttpUri(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;

    // The origin of an absolute http or https URL, as a browser serializes it in a message's origin: a host name in its
    // ASCII (punycode) form, so that an internationalized name matches; null for any other.
    public static string? OriginOf(string? url) => OriginOf(HttpUri(url));

    public static string? OriginOf(Uri? uri) => uri is null
        ? null
        : string.Create(CultureInfo.InvariantCulture, $"{uri.Scheme}://{(uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : uri.IdnHost)}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}");

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
            "ready" => new PreviewMessage(PreviewMessageType.Ready, Text(message, "url")),
            "navigated" => new PreviewMessage(PreviewMessageType.Navigated, Text(message, "url")),
            _ => null,
        };
    }

    // The draft was saved: the page reads it again.
    public static string Refresh(string type, Guid id, string? locale, int version) => new JsonObject
    {
        ["protocol"] = Protocol,
        ["version"] = Version,
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
