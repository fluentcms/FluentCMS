using System.Security.Cryptography;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace FluentCMS.Core.Identity.Oidc;

// The keys of cms_signing_key, generated the first time one is asked for and the same for every instance after that. The
// first key of a use has a fixed id, so instances that start together on an empty table race for one row: the insert that
// loses fails on the primary key and reads the winner's key. Each instance reads its keys once, so they must agree then.
internal sealed class SigningKeyStore(IServiceScopeFactory scopes, IDataProtectionProvider protection, TimeProvider time)
{
    private const int RsaKeySize = 2048;
    private const int EncryptionKeySize = 32;

    private static readonly Guid FirstSigningKey = new("0195f1c0-0000-7000-8000-000000000001");
    private static readonly Guid FirstEncryptionKey = new("0195f1c0-0000-7000-8000-000000000002");

    private readonly IDataProtector _protector = protection.CreateProtector("FluentCMS.SigningKeys");

    public IReadOnlyList<SecurityKey> Keys(string use)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var stored = Stored(context, use);
        if (stored.Count == 0)
        {
            var generated = Generate(use);
            context.Add(generated);
            try
            {
                context.SaveChanges();
            }
            catch (DbUpdateException) when (Exists(generated.Id))
            {
                // Another instance stored the first key in the meantime; it is the one to use.
            }

            context.ChangeTracker.Clear();
            stored = Stored(context, use);
        }

        return [.. stored.Select(Read)];
    }

    // The oldest first, so that every instance signs with the same key. Sorted here because SQLite cannot order by a
    // DateTimeOffset, and there are few rows.
    private static List<SigningKey> Stored(SystemDbContext context, string use) =>
        [.. context.Set<SigningKey>().Where(key => key.Use == use).AsEnumerable().OrderBy(key => key.CreatedAt).ThenBy(key => key.Id)];

    private bool Exists(Guid id)
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<SigningKey>().Any(key => key.Id == id);
    }

    private SigningKey Generate(string use)
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
                Id = use == SigningKey.Signing ? FirstSigningKey : FirstEncryptionKey,
                Use = use,
                Algorithm = algorithm,
                ProtectedKey = Convert.ToBase64String(_protector.Protect(material)),
                CreatedAt = time.GetUtcNow(),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private SecurityKey Read(SigningKey row)
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

        key.KeyId = row.Id.ToString("N");
        return key;
    }
}

// Whether tokens are signed with a key the server generated, which Production refuses unless the host allows it.
internal sealed class SigningKeyStatus(SecurityOptions security) : ISigningKeyStatus
{
    public bool IsGenerated => !security.SigningCertificate.IsConfigured;
}

// Hands OpenIddict its keys when its options are first built, which is on the first request, after the migrations ran.
internal sealed class ServerCredentials(SecurityOptions security, SigningKeyStore store) : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        if (security.SigningCertificate.IsConfigured)
        {
            options.SigningCredentials.Add(new SigningCredentials(new X509SecurityKey(security.SigningCertificate.Load()), SecurityAlgorithms.RsaSha256));
        }
        else
        {
            foreach (var key in store.Keys(SigningKey.Signing))
            {
                options.SigningCredentials.Add(new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            }
        }

        foreach (var key in store.Keys(SigningKey.Encryption))
        {
            options.EncryptionCredentials.Add(new EncryptingCredentials(key, SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512));
        }
    }
}
