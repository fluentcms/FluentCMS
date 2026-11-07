using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace FluentCMS.Plugins.Webhooks;

// The secrets and the headers of webhooks, stored protected with Data Protection under a purpose each, and the signature
// of a delivery.
internal sealed class WebhookSigning(IDataProtectionProvider protection)
{
    public const int SecretMinLength = 16;
    public const int SecretMaxLength = 256;

    private readonly IDataProtector _secrets = protection.CreateProtector("FluentCMS.Plugins.Webhooks.Secret");
    private readonly IDataProtector _headers = protection.CreateProtector("FluentCMS.Plugins.Webhooks.Headers");

    public static string NewSecret() => "whsec_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    // X-FluentCMS-Signature: t=<unix seconds>,v1=<hex HMAC-SHA256(secret, t + "." + body)>, with a v1 for each secret.
    public static string Signature(long timestamp, string body, params IReadOnlyList<string> secrets)
    {
        var t = timestamp.ToString(CultureInfo.InvariantCulture);
        var signed = Encoding.UTF8.GetBytes(t + "." + body);
        var header = new StringBuilder("t=").Append(t);
        foreach (var secret in secrets)
        {
            header.Append(",v1=").Append(Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed)));
        }

        return header.ToString();
    }

    public string Protect(string secret) => _secrets.Protect(secret);

    public string Unprotect(string stored) => _secrets.Unprotect(stored);

    // Null when the key ring no longer has the key that protected the value; the same for TryUnprotectHeaders.
    public string? TryUnprotect(string stored)
    {
        try
        {
            return _secrets.Unprotect(stored);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public string ProtectHeaders(IReadOnlyDictionary<string, string> headers) => _headers.Protect(WebhookJson.Of(headers));

    // Rows written before the headers were protected hold them as a plain JSON object, which a protected value, in
    // base64url, never starts with.
    public IReadOnlyDictionary<string, string> UnprotectHeaders(string stored) => WebhookJson.Map(stored.StartsWith('{') ? stored : _headers.Unprotect(stored));

    public IReadOnlyDictionary<string, string>? TryUnprotectHeaders(string stored)
    {
        try
        {
            return UnprotectHeaders(stored);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
