using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace FluentCMS.Core.Identity.Oidc;

// Hands OpenIddict its keys each time its options are built: first on the first request, after the migrations ran, and
// again whenever SigningKeyRing says the keys or their states changed. Every key that has not retired is accepted, and only
// the active one of a use signs or encrypts (ADR 0008). OpenIddict signs and encrypts with the first credential of its
// lists, which it sorts without telling two generated keys apart, so the active credential is marked here and put first
// after that sort. A signing key that only validates is handed over without its private half, so that a wrong order would
// fail a signature and never sign with the wrong key.
internal sealed class ServerCredentials(SigningCertificates certificates, SigningKeyRing ring, TimeProvider time)
    : IConfigureOptions<OpenIddictServerOptions>, IPostConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        var set = ring.Current;
        var now = time.GetUtcNow();
        var certificate = certificates.SigningKey;
        if (certificate is not null)
        {
            options.SigningCredentials.Add(new ActiveSigningCredentials(certificate));
        }

        foreach (var validates in certificates.ValidationKeys)
        {
            options.SigningCredentials.Add(new SigningCredentials(validates, SecurityAlgorithms.RsaSha256));
        }

        // While a certificate signs, the generated signing keys still in the table are accepted until they retire.
        var signing = Accepted(set, SigningKey.Signing, now);
        var signs = certificate is not null ? null : SigningKeyStates.ActiveOf(signing, now);
        foreach (var row in signing)
        {
            if (Key(set, row, row == signs) is RsaSecurityKey key)
            {
                options.SigningCredentials.Add(row == signs
                    ? new ActiveSigningCredentials(key)
                    : new SigningCredentials(PublicHalf(key), SecurityAlgorithms.RsaSha256));
            }
        }

        var encryption = Accepted(set, SigningKey.Encryption, now);
        var encrypts = SigningKeyStates.ActiveOf(encryption, now);
        foreach (var row in encryption)
        {
            if (Key(set, row, row == encrypts) is { } key)
            {
                options.EncryptionCredentials.Add(row == encrypts
                    ? new ActiveEncryptingCredentials(key)
                    : new EncryptingCredentials(key, SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512));
            }
        }
    }

    // After OpenIddict's own, which sorts the lists.
    public void PostConfigure(string? name, OpenIddictServerOptions options)
    {
        MoveFirst(options.SigningCredentials, credentials => credentials is ActiveSigningCredentials);
        MoveFirst(options.EncryptionCredentials, credentials => credentials is ActiveEncryptingCredentials);
    }

    private static List<SigningKey> Accepted(SigningKeySet set, string use, DateTimeOffset now) =>
        [.. set.Rows.Where(key => key.Use == use && !SigningKeyStates.IsRetired(key, now))];

    // A key the key ring could not read is left out, and SigningKeyRing has logged it. When it is the active one nothing may
    // be signed or encrypted with another in its place, so the options are not built and the server issues nothing.
    private static SecurityKey? Key(SigningKeySet set, SigningKey row, bool active)
    {
        if (set.Keys.TryGetValue(row.Id, out var key))
        {
            return key;
        }

        return active
            ? throw new InvalidOperationException($"The Data Protection key ring cannot read the active key {row.Id:D} (use {row.Use}) of cms_signing_key, so the authorization server issues nothing. Restore the ring key or certificate that protected it, or run 'fluentcms keys revoke --force'.")
            : null;
    }

    private static RsaSecurityKey PublicHalf(RsaSecurityKey key) =>
        new(new RSAParameters { Modulus = key.Parameters.Modulus, Exponent = key.Parameters.Exponent }) { KeyId = key.KeyId };

    private static void MoveFirst<T>(List<T> credentials, Predicate<T> active)
    {
        var index = credentials.FindIndex(active);
        if (index > 0)
        {
            var first = credentials[index];
            credentials.RemoveAt(index);
            credentials.Insert(0, first);
        }
    }

    private sealed class ActiveSigningCredentials(SecurityKey key) : SigningCredentials(key, SecurityAlgorithms.RsaSha256);

    private sealed class ActiveEncryptingCredentials(SecurityKey key) : EncryptingCredentials(key, SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512);
}
