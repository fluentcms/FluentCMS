using System.Security.Cryptography;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace FluentCMS.Core.Identity.Oidc;

// The keys of cms_signing_key. A use gets its first key the first time one is asked for, and a new one whenever none of
// its keys is left that has not retired; that key is active at once. Instances that start together on an empty table race
// for one row: each inserts the next sequence of the use, the unique index lets one in, and the others read the winner's
// key. The ids are random, so a kid never names two keys.
internal sealed class SigningKeyStore(IServiceScopeFactory scopes, IDataProtectionProvider protection, TimeProvider time)
{
    private const int RsaKeySize = 2048;
    private const int EncryptionKeySize = 32;

    private readonly IDataProtector _protector = protection.CreateProtector("FluentCMS.SigningKeys");

    // Every row, by use and sequence, after making a key for each of the uses that has none that has not retired.
    public IReadOnlyList<SigningKey> Read(IReadOnlyCollection<string> uses)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var stored = Stored(context);
        foreach (var use in uses)
        {
            var now = time.GetUtcNow();
            if (stored.Any(key => key.Use == use && !SigningKeyStates.IsRetired(key, now)))
            {
                continue;
            }

            var generated = Generate(use, NextSequence(stored, use), now, activeFrom: now);
            context.Add(generated);
            try
            {
                context.SaveChanges();
            }
            catch (DbUpdateException) when (Exists(use, generated.Sequence))
            {
                // Another instance stored the key of this sequence in the meantime; it is the one to use.
            }

            context.ChangeTracker.Clear();
            stored = Stored(context);
        }

        return stored;
    }

    public SecurityKey Unprotect(SigningKey row)
    {
        var material = _protector.Unprotect(Convert.FromBase64String(row.ProtectedKey));
        SecurityKey key;
        if (row.Use == SigningKey.Signing)
        {
            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(material, out _);
            CryptographicOperations.ZeroMemory(material);
            key = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: true));
        }
        else
        {
            key = new SymmetricSecurityKey(material);
        }

        key.KeyId = KeyId(row);
        return key;
    }

    public static string KeyId(SigningKey row) => row.Id.ToString("N");

    private static int NextSequence(IEnumerable<SigningKey> stored, string use) =>
        stored.Where(key => key.Use == use).Select(key => key.Sequence).DefaultIfEmpty().Max() + 1;

    private static List<SigningKey> Stored(SystemDbContext context) =>
        [.. context.Set<SigningKey>().AsNoTracking().OrderBy(key => key.Use).ThenBy(key => key.Sequence)];

    private bool Exists(string use, int sequence)
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<SigningKey>().Any(key => key.Use == use && key.Sequence == sequence);
    }

    private SigningKey Generate(string use, int sequence, DateTimeOffset now, DateTimeOffset activeFrom)
    {
        byte[] material;
        string algorithm;
        if (use == SigningKey.Signing)
        {
            using var rsa = RSA.Create(RsaKeySize);
            material = rsa.ExportRSAPrivateKey();
            algorithm = SecurityAlgorithms.RsaSha256;
        }
        else
        {
            material = RandomNumberGenerator.GetBytes(EncryptionKeySize);
            algorithm = SecurityAlgorithms.Aes256KW;
        }

        try
        {
            return new SigningKey
            {
                Id = Guid.NewGuid(),
                Use = use,
                Sequence = sequence,
                Algorithm = algorithm,
                ProtectedKey = Convert.ToBase64String(_protector.Protect(material)),
                CreatedAt = now,
                ActiveFrom = activeFrom,
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }
}

// Whether tokens are signed with a key the server generated, which Production refuses unless the host allows it.
internal sealed class SigningKeyStatus(SecurityOptions security) : ISigningKeyStatus
{
    public bool IsGenerated => !security.SigningCertificate.IsConfigured;
}
