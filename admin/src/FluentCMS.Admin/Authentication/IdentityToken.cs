using System.Buffers.Text;
using System.Text.Json;

namespace FluentCMS.Admin.Authentication;

// The claims of the id token of a sign-in. Its signature is not checked: the token comes straight from the token endpoint of
// the discovered issuer over TLS, in answer to a code the PKCE verifier of this tab redeemed, which OpenID Connect Core
// 3.1.3.7 accepts in place of the signature; the .NET runtime on WebAssembly has no RSA, and the browser's SubtleCrypto
// would take more interop. What is checked is that the issuer, the audience, the authorized party, the nonce and the expiry
// are the ones of this sign-in. A claim of the wrong shape refuses the token, never throws.
internal static class IdentityToken
{
    // Clocks of browser and server may differ a little.
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    public static SignedInUser? Read(string? token, string issuer, string clientId, string nonce, DateTimeOffset now)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 3 } || !Base64Url.IsValid(parts[1]))
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            var claims = payload.RootElement;
            if (claims.ValueKind != JsonValueKind.Object
                || !Text(claims, "iss", out var iss) || iss != issuer
                || !Text(claims, "azp", out var party)
                || !IsForClient(claims, clientId, party)
                || !Text(claims, "nonce", out var sentNonce) || !ProofKey.SameSecret(sentNonce, nonce)
                || !claims.TryGetProperty("exp", out var exp) || exp.ValueKind != JsonValueKind.Number || !exp.TryGetInt64(out var expires)
                || DateTimeOffset.FromUnixTimeSeconds(expires) + ClockSkew < now
                || !Text(claims, "sub", out var subject) || string.IsNullOrEmpty(subject)
                || !Text(claims, "name", out var name)
                || !Text(claims, "email", out var email))
            {
                return null;
            }

            return new SignedInUser(subject, name, email);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentOutOfRangeException or FormatException)
        {
            return null;
        }
    }

    // False when the claim is there but not a string, or a string that does not read, such as one with a lone surrogate; a
    // missing claim is true with null.
    private static bool Text(JsonElement claims, string name, out string? value)
    {
        value = null;
        if (!claims.TryGetProperty(name, out var claim))
        {
            return true;
        }

        if (claim.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            value = claim.GetString();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // OpenID Connect Core 3.1.3.7: the client is the one audience, or one of several when azp names it; an azp that names
    // another party refuses the token in either case.
    private static bool IsForClient(JsonElement claims, string clientId, string? party)
    {
        if (party is not null && party != clientId)
        {
            return false;
        }

        if (!claims.TryGetProperty("aud", out var audience))
        {
            return false;
        }

        if (audience.ValueKind == JsonValueKind.String)
        {
            return Text(claims, "aud", out var single) && single == clientId;
        }

        if (audience.ValueKind != JsonValueKind.Array || audience.GetArrayLength() == 0)
        {
            return false;
        }

        var audiences = new List<string?>();
        foreach (var item in audience.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            try
            {
                audiences.Add(item.GetString());
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return audiences.Contains(clientId) && (audiences.Count == 1 || party == clientId);
    }
}
