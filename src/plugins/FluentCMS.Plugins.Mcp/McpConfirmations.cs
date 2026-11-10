using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FluentCMS.Content;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace FluentCMS.Plugins.Mcp;

// One entry of a plan: what a confirmation is bound to.
internal sealed record McpPlanEntry(Guid Id, int? Version, string? Status);

// What a dry run answered for: the call and the entries it would touch.
internal sealed record McpPlan(OperationContext Operation, string Tool, JsonElement Arguments, IReadOnlyList<McpPlanEntry> Entries);

internal enum McpConfirmation
{
    Confirmed,
    Missing,
    Invalid,
    Expired,
    PlanChanged,
}

// The confirmation token of a dry run (spec 5.4): an HMAC-SHA256 over the plan, which is the tool, its arguments, the
// principal, the client, the site and locale of the call, the time the token ends and the ids, versions and statuses of
// the entries. The key is 256 random bits of this instance that travel in the token wrapped by Data Protection, so an
// instance that shares the key ring unwraps the key and checks the token of another, and no table holds anything. Only
// the key ring makes a wrapped key, under a purpose nothing else uses, so a caller cannot bring a key of its own, and
// without the key it cannot sign another plan.
//
// The token is v1.{expires}.{entries}.{key}.{mac}: the Unix second it ends, the SHA-256 of the entries, the wrapped key
// and the MAC, which covers the two before it with the rest of the plan. The hash of the entries is in the token so that
// a plan that changed is told apart from a token of another call.
internal sealed class McpConfirmations(IDataProtectionProvider protection, TimeProvider time)
{
    public const string DryRun = "dryRun";
    public const string Token = "confirmationToken";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private const string Version = "v1";
    private const int KeyLength = 32;

    // A token is some 260 characters; one far longer is not read.
    private const int MaxTokenLength = 2048;

    private readonly IDataProtector _protector = protection.CreateProtector("FluentCMS.Plugins.Mcp.ConfirmationKey");
    private readonly Lock _gate = new();
    private byte[]? _key;
    private string? _wrapped;

    public (string Token, DateTimeOffset ExpiresAt) Issue(McpPlan plan)
    {
        var (key, wrapped) = Own();
        var expires = time.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        var entries = WebEncoders.Base64UrlEncode(EntriesHash(plan.Entries));
        var mac = WebEncoders.Base64UrlEncode(HMACSHA256.HashData(key, Signed(plan, expires, entries)));
        return (string.Join('.', Version, expires.ToString(CultureInfo.InvariantCulture), entries, wrapped, mac), DateTimeOffset.FromUnixTimeSeconds(expires));
    }

    // The MAC is checked first, so the time and the entries it is then read by are the ones this server signed.
    public McpConfirmation Check(string? token, McpPlan plan)
    {
        if (token is not { Length: > 0 })
        {
            return McpConfirmation.Missing;
        }

        if (token.Length > MaxTokenLength
            || token.Split('.') is not [Version, var expiresText, var entries, var wrapped, var macText]
            || !long.TryParse(expiresText, NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            || Decode(macText) is not { } mac
            || Decode(entries) is not { } signedEntries
            || KeyOf(wrapped) is not { } key)
        {
            return McpConfirmation.Invalid;
        }

        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, Signed(plan, expires, entries)), mac))
        {
            return McpConfirmation.Invalid;
        }

        if (time.GetUtcNow().ToUnixTimeSeconds() >= expires)
        {
            return McpConfirmation.Expired;
        }

        return CryptographicOperations.FixedTimeEquals(EntriesHash(plan.Entries), signedEntries) ? McpConfirmation.Confirmed : McpConfirmation.PlanChanged;
    }

    private (byte[] Key, string Wrapped) Own()
    {
        lock (_gate)
        {
            if (_key is null)
            {
                var key = RandomNumberGenerator.GetBytes(KeyLength);
                _wrapped = WebEncoders.Base64UrlEncode(_protector.Protect(key));
                _key = key;
            }

            return (_key, _wrapped!);
        }
    }

    private byte[]? KeyOf(string wrapped)
    {
        lock (_gate)
        {
            if (_key is not null && string.Equals(wrapped, _wrapped, StringComparison.Ordinal))
            {
                return _key;
            }
        }

        try
        {
            return Decode(wrapped) is { } bytes && _protector.Unprotect(bytes) is { Length: KeyLength } key ? key : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static byte[]? Decode(string text)
    {
        try
        {
            return WebEncoders.Base64UrlDecode(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // In the order of their ids, so that the order a query returned them in does not change the plan.
    private static byte[] EntriesHash(IReadOnlyList<McpPlanEntry> entries)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var entry in entries.OrderBy(entry => entry.Id))
            {
                writer.WriteStartArray();
                writer.WriteStringValue(entry.Id);
                if (entry.Version is { } version)
                {
                    writer.WriteNumberValue(version);
                }
                else
                {
                    writer.WriteNullValue();
                }

                writer.WriteStringValue(entry.Status);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        return SHA256.HashData(buffer.WrittenSpan);
    }

    // One JSON object, so that no two plans share their bytes.
    private static byte[] Signed(McpPlan plan, long expires, string entries)
    {
        var operation = plan.Operation;
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("tool", plan.Tool);
            writer.WritePropertyName("arguments");
            WriteArguments(writer, plan.Arguments);
            writer.WriteString("principal", operation.Principal.ToString());
            writer.WriteString("client", operation.ClientId);
            writer.WriteString("site", operation.Site.Value);
            writer.WriteString("locale", operation.Locale);
            writer.WriteNumber("expires", expires);
            writer.WriteString("entries", entries);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    // The arguments without the two that tell a dry run from its run, and without those left out as null.
    private static void WriteArguments(Utf8JsonWriter writer, JsonElement arguments)
    {
        writer.WriteStartObject();
        foreach (var property in arguments.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (property.Name is DryRun or Token || property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            writer.WritePropertyName(property.Name);
            WriteCanonical(writer, property.Value);
        }

        writer.WriteEndObject();
    }

    // The keys of an object in one order, so that the same arguments sign the same however a client wrote them.
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
