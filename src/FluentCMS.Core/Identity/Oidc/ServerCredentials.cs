using System.Security.Cryptography;
using FluentCMS.Core.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace FluentCMS.Core.Identity.Oidc;

// Hands OpenIddict its keys when its options are built, which is first on the first request, after the migrations ran.
// Every key that has not retired is accepted, and only the active one of a use signs or encrypts (ADR 0008). OpenIddict
// signs and encrypts with the first credential of its lists, which it sorts without telling two generated keys apart, so
// the active credential is marked here and put first after that sort. A signing key that only validates is handed over
// without its private half, so that a wrong order would fail a signature and never sign with the wrong key.
internal sealed class ServerCredentials(SecurityOptions security, SigningKeyStore store, TimeProvider time)
    : IConfigureOptions<OpenIddictServerOptions>, IPostConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        var certificate = security.SigningCertificate.IsConfigured;
        var stored = store.Read(certificate ? [SigningKey.Encryption] : SigningKey.Uses);
        var now = time.GetUtcNow();

        if (certificate)
        {
            options.SigningCredentials.Add(new ActiveSigningCredentials(new X509SecurityKey(security.SigningCertificate.Load())));
        }

        var signing = Accepted(stored, SigningKey.Signing, now);
        var signs = certificate ? null : SigningKeyStates.ActiveOf(signing, now);
        foreach (var row in signing)
        {
            var key = (RsaSecurityKey)store.Unprotect(row);
            options.SigningCredentials.Add(row == signs
                ? new ActiveSigningCredentials(key)
                : new SigningCredentials(PublicHalf(key), SecurityAlgorithms.RsaSha256));
        }

        var encryption = Accepted(stored, SigningKey.Encryption, now);
        var encrypts = SigningKeyStates.ActiveOf(encryption, now);
        foreach (var row in encryption)
        {
            var key = store.Unprotect(row);
            options.EncryptionCredentials.Add(row == encrypts
                ? new ActiveEncryptingCredentials(key)
                : new EncryptingCredentials(key, SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512));
        }
    }

    // After OpenIddict's own, which sorts the lists.
    public void PostConfigure(string? name, OpenIddictServerOptions options)
    {
        MoveFirst(options.SigningCredentials, credentials => credentials is ActiveSigningCredentials);
        MoveFirst(options.EncryptionCredentials, credentials => credentials is ActiveEncryptingCredentials);
    }

    private static List<SigningKey> Accepted(IEnumerable<SigningKey> stored, string use, DateTimeOffset now) =>
        [.. stored.Where(key => key.Use == use && !SigningKeyStates.IsRetired(key, now))];

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
