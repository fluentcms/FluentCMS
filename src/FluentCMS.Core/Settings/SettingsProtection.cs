using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Settings;

internal enum StoredSecrets
{
    Protected,
    Plaintext,
    Unreadable,
}

// The secret keys of a stored document, protected with Data Protection under a purpose per area, key and scope, so that a
// reader of the database, a backup or a replica holds no secret, and a value cannot be moved to another key, area or scope
// and still be read. A protected value is a JSON string with a prefix that a value in base64url never has; anything else
// under a secret key is a value written before secrets were protected, which is read as it is and protected by the next
// write of its document.
internal sealed class SettingsProtection(IDataProtectionProvider provider, ILogger<SettingsProtection> logger)
{
    public const string Prefix = "fcms-protected:v1:";

    private const string Purpose = "FluentCMS.Settings.Secret";

    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    // The document as it is stored: every secret key that holds a value, null aside, protected. A key of unreadable holds a
    // value the key ring could not unprotect when the row was read; it goes back as it was, unless the document names the key.
    public string Protect(SettingsDescriptor descriptor, string json, Guid? site, IReadOnlyDictionary<string, string>? unreadable = null, string? named = null)
    {
        if (descriptor.SecretKeys.Count == 0 || JsonNode.Parse(json) is not JsonObject document)
        {
            return json;
        }

        foreach (var key in descriptor.SecretKeys)
        {
            if (document[key] is { } value)
            {
                document[key] = Prefix + Protector(descriptor, key, site).Protect(value.ToJsonString());
            }
        }

        var sent = named is null ? null : JsonNode.Parse(named, documentOptions: new JsonDocumentOptions { MaxDepth = SettingsJson.MaxDepth }) as JsonObject;
        foreach (var (key, stored) in unreadable ?? None)
        {
            if (sent is null || !sent.ContainsKey(key))
            {
                document[key] = stored;
            }
        }

        return document.ToJsonString();
    }

    public string Unprotect(SettingsDescriptor descriptor, string json, Guid? site) => Unprotect(descriptor, json, site, out _);

    // The document as the class reads it. A value that the key ring cannot unprotect, because its key is gone or the value was
    // changed or moved, is left out of the document and returned in unreadable as it is stored, and the log names the key.
    public string Unprotect(SettingsDescriptor descriptor, string json, Guid? site, out IReadOnlyDictionary<string, string> unreadable)
    {
        unreadable = None;
        if (descriptor.SecretKeys.Count == 0 || Parse(json) is not JsonObject document)
        {
            return json;
        }

        var changed = false;
        Dictionary<string, string>? lost = null;
        foreach (var key in descriptor.SecretKeys)
        {
            if (!IsProtected(document[key], out var stored))
            {
                continue;
            }

            changed = true;
            if (TryUnprotect(descriptor, key, site, stored) is { } value)
            {
                document[key] = value;
            }
            else
            {
                document.Remove(key);
                (lost ??= new(StringComparer.Ordinal))[key] = stored;
                SettingsLog.SecretNotUnprotected(logger, descriptor.Area, key, ScopeName(site));
            }
        }

        unreadable = lost ?? None;
        return changed ? document.ToJsonString() : json;
    }

    // What a stored document holds under its secret keys, worst first: a value the key ring cannot unprotect, then one written
    // before secrets were protected. Nothing is logged.
    public StoredSecrets Inspect(SettingsDescriptor descriptor, string json, Guid? site)
    {
        if (descriptor.SecretKeys.Count == 0 || Parse(json) is not JsonObject document)
        {
            return StoredSecrets.Protected;
        }

        var found = StoredSecrets.Protected;
        foreach (var key in descriptor.SecretKeys)
        {
            if (IsProtected(document[key], out var stored))
            {
                if (TryUnprotect(descriptor, key, site, stored) is null)
                {
                    return StoredSecrets.Unreadable;
                }
            }
            else if (document[key] is not null)
            {
                found = StoredSecrets.Plaintext;
            }
        }

        return found;
    }

    private JsonNode? TryUnprotect(SettingsDescriptor descriptor, string key, Guid? site, string stored)
    {
        try
        {
            return JsonNode.Parse(Protector(descriptor, key, site).Unprotect(stored[Prefix.Length..]));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private IDataProtector Protector(SettingsDescriptor descriptor, string key, Guid? site) =>
        provider.CreateProtector(Purpose, descriptor.Area, key, site is { } one ? one.ToString("D") : "instance");

    private static string ScopeName(Guid? site) => site is { } one ? $"site {one}" : "the instance";

    private static bool IsProtected(JsonNode? value, out string stored)
    {
        stored = "";
        if (value is JsonValue text && text.TryGetValue<string>(out var content) && content.StartsWith(Prefix, StringComparison.Ordinal))
        {
            stored = content;
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
