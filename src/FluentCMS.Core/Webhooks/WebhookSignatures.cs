using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FluentCMS.Core.Webhooks;

// The Standard Webhooks signature: HMAC-SHA256 of "id.timestamp.body" keyed with the secret's bytes, sent as "v1," and base64 in the
// webhook-signature header beside webhook-id and webhook-timestamp, so a receiver can verify with an existing library.
public static class WebhookSignatures
{
    public const string IdHeader = "webhook-id";
    public const string TimestampHeader = "webhook-timestamp";
    public const string SignatureHeader = "webhook-signature";

    public static string Sign(string secret, string id, DateTimeOffset timestamp, string body)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(body);

        if (!secret.StartsWith(WebhookSecrets.Prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"A webhook secret starts with {WebhookSecrets.Prefix}.", nameof(secret));
        }

        var key = Convert.FromBase64String(secret[WebhookSecrets.Prefix.Length..]);
        var signed = Encoding.UTF8.GetBytes($"{id}.{Timestamp(timestamp)}.{body}");

        return "v1," + Convert.ToBase64String(HMACSHA256.HashData(key, signed));
    }

    // Whole seconds since the Unix epoch, as the header carries them.
    public static string Timestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }
}
