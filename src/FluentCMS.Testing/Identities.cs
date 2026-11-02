using System.Security.Cryptography;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Testing;

// Users and API keys do not exist as stored identities before M2, so what a fixture can do is build the principal that the
// M2 authentication handlers will build: the kind, the roles claim naming a role that exists in cms_role, and the scope of
// the token or key class. It refuses a role that is not there, which production cannot reach either. When M2 lands, this
// is the one place that calls the user manager and the key service instead.
internal static class Identities
{
    private static readonly string[] BuiltInNames =
    [
        BuiltInRoles.SuperAdminName,
        BuiltInRoles.EditorName,
        BuiltInRoles.AuthorName,
        BuiltInRoles.PublicName,
        BuiltInRoles.MemberName,
    ];

    public static async Task<Role> FindRoleAsync(IServiceProvider services, string name, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        var roles = await context.Set<Role>().AsNoTracking()
            .Where(role => role.Name == name && (role.SiteId == null || role.SiteId == DefaultSite.Id))
            .ToListAsync(cancellationToken);

        return roles.OrderByDescending(role => role.SiteId == DefaultSite.Id).FirstOrDefault()
            ?? throw new InvalidOperationException($"There is no role named '{name}'. The built-in ones are {string.Join(", ", BuiltInNames)}; CreateRoleAsync adds another.");
    }

    public static async Task<TestUser> CreateUserAsync(IServiceProvider services, string role, string? name, CancellationToken cancellationToken)
    {
        var stored = await FindRoleAsync(services, role, cancellationToken);
        var id = Guid.CreateVersion7();
        var userName = name ?? $"{role} {id.ToString("N")[^6..]}";
        var email = $"{id:N}@test.fluentcms.local";

        // An admin user signs in to the admin, whose tokens carry the management scope.
        var principal = new Principal(PrincipalKind.AdminUser, id, userName, Claims(stored.Id, TokenScopes.Management));
        return new TestUser(id, userName, email, stored.Name, stored.Id, principal);
    }

    public static async Task<TestApiKey> CreateApiKeyAsync(IServiceProvider services, ApiKeyClass keyClass, string role, string? name, CancellationToken cancellationToken)
    {
        var stored = await FindRoleAsync(services, role, cancellationToken);
        var id = Guid.CreateVersion7();
        var keyName = name ?? $"{keyClass} key {id.ToString("N")[^6..]}";
        var secret = Prefix(keyClass) + Base64Url(RandomNumberGenerator.GetBytes(32));

        var principal = new Principal(PrincipalKind.ApiKey, id, keyName, Claims(stored.Id, Scope(keyClass)));
        return new TestApiKey(id, keyName, keyClass, stored.Name, stored.Id, secret, principal);
    }

    private static Dictionary<string, string> Claims(Guid roleId, string scope) => new()
    {
        [ClaimPrincipalRoles.Claim] = roleId.ToString(),
        [TokenScopes.Claim] = scope,
    };

    private static string Scope(ApiKeyClass keyClass) => keyClass switch
    {
        ApiKeyClass.Delivery => TokenScopes.Delivery,
        ApiKeyClass.Preview => TokenScopes.Preview,
        ApiKeyClass.Management => TokenScopes.Management,
        _ => throw new ArgumentOutOfRangeException(nameof(keyClass), keyClass, null),
    };

    private static string Prefix(ApiKeyClass keyClass) => keyClass switch
    {
        ApiKeyClass.Delivery => "fcms_dk_",
        ApiKeyClass.Preview => "fcms_pk_",
        ApiKeyClass.Management => "fcms_mk_",
        _ => throw new ArgumentOutOfRangeException(nameof(keyClass), keyClass, null),
    };

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
