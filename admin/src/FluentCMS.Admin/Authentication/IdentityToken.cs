using System.Buffers.Text;
using System.Text.Json;

namespace FluentCMS.Admin.Authentication;

// The claims of the id token of a sign-in. Its signature is not checked: the token comes straight from the token endpoint of
// the discovered issuer over TLS, in answer to a code the PKCE verifier of this tab redeemed, which OpenID Connect Core
// 3.1.3.7 accepts in place of the signature, and the browser has no RSA to check it with. What is checked is that the issuer,
// the audience, the authorized party, the nonce and the expiry are the ones of this sign-in.
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
                || Text(claims, "iss") != issuer
                || !HasAudience(claims, clientId)
                || (Text(claims, "azp") is { } party && party != clientId)
                || !ProofKey.SameSecret(Text(claims, "nonce"), nonce)
                || !claims.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expires)
                || DateTimeOffset.FromUnixTimeSeconds(expires) + ClockSkew < now
                || Text(claims, "sub") is not { Length: > 0 } subject)
            {
                return null;
            }

            return new SignedInUser(subject, Text(claims, "name"), Text(claims, "email"));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement claims, string name) =>
        claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool HasAudience(JsonElement claims, string clientId)
    {
        if (!claims.TryGetProperty("aud", out var audience))
        {
            return false;
        }

        return audience.ValueKind switch
        {
            JsonValueKind.String => audience.GetString() == clientId,
            JsonValueKind.Array => audience.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == clientId),
            _ => false,
        };
    }
}
