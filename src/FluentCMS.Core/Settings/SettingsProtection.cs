using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Settings;

// The secret keys of a stored document, protected with Data Protection under a purpose per area and key, so that a reader of
// the database, a backup or a replica holds no secret, and a value cannot be moved to another key or area and still be read.
// A protected value is a JSON string with a prefix that a value in base64url never has; anything else under a secret key is
// a value written before secrets were protected, which is read as it is and protected by the next write of its document.
internal sealed class SettingsProtection(IDataProtectionProvider provider, ILogger<SettingsProtection> logger)
{
    public const string Prefix = "fcms-protected:v1:";

    private const string Purpose = "FluentCMS.Settings.Secret";

    // The document as it is stored: every secret key that holds a value, null aside, protected.
    public string Protect(SettingsDescriptor descriptor, string json)
    {
        if (descriptor.SecretKeys.Count == 0 || JsonNode.Parse(json) is not JsonObject document)
        {
            return json;
        }

        foreach (var key in descriptor.SecretKeys)
        {
            if (document[key] is { } value)
            {
                document[key] = Prefix + Protector(descriptor, key).Protect(value.ToJsonString());
            }
        }

        return document.ToJsonString();
    }

    // The document as the class reads it. A value that the key ring cannot unprotect, because its key is gone or the value was
    // changed or moved, is left out of the document, as if it had never been stored, and the log names the key.
    public string Unprotect(SettingsDescriptor descriptor, string json, string scope)
    {
        if (descriptor.SecretKeys.Count == 0 || Parse(json) is not JsonObject document)
        {
            return json;
        }

        var changed = false;
        foreach (var key in descriptor.SecretKeys)
        {
            if (!IsProtected(document[key], out var payload))
            {
                continue;
            }

            changed = true;
            try
            {
                document[key] = JsonNode.Parse(Protector(descriptor, key).Unprotect(payload));
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
            {
                document.Remove(key);
                SettingsLog.SecretNotUnprotected(logger, descriptor.Area, key, scope);
            }
        }

        return changed ? document.ToJsonString() : json;
    }

    // Whether a stored document holds a secret value that is not protected: one written before secrets were.
    public static bool HoldsPlaintext(SettingsDescriptor descriptor, string json) =>
        descriptor.SecretKeys.Count > 0
        && Parse(json) is JsonObject document
        && descriptor.SecretKeys.Any(key => document[key] is { } value && !IsProtected(value, out _));

    private IDataProtector Protector(SettingsDescriptor descriptor, string key) => provider.CreateProtector(Purpose, descriptor.Area, key);

    private static bool IsProtected(JsonNode? value, out string payload)
    {
        payload = "";
        if (value is JsonValue text && text.TryGetValue<string>(out var content) && content.StartsWith(Prefix, StringComparison.Ordinal))
        {
            payload = content[Prefix.Length..];
            return true;
        }

        return false;
    }

    private static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
