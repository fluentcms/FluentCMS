using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace FluentCMS.Plugins.Webhooks;

// The secrets of webhooks, stored protected with Data Protection, and the signature of a delivery.
internal sealed class WebhookSigning(IDataProtectionProvider protection)
{
    public const int SecretMinLength = 16;
    public const int SecretMaxLength = 256;

    private readonly IDataProtector _protector = protection.CreateProtector("FluentCMS.Plugins.Webhooks.Secret");

    public static string NewSecret() => "whsec_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    // X-FluentCMS-Signature: t=<unix seconds>,v1=<hex HMAC-SHA256(secret, t + "." + body)>.
    public static string Signature(string secret, long timestamp, string body)
    {
        var t = timestamp.ToString(CultureInfo.InvariantCulture);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(t + "." + body));
        return $"t={t},v1={Convert.ToHexStringLower(mac)}";
    }

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string stored) => _protector.Unprotect(stored);
}
