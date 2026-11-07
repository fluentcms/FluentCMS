using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Testing;

// A user is stored through the user manager and holds its role in cms_user_role; its principal is what the authorization
// server will build, with the management scope and no roles claim. A key is stored through the key service, and its
// principal is the one its handler's claims become. A role that is not there is refused, which production cannot reach either.
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

        await using (var scope = services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            // The display name has spaces, which a user name may not.
            var user = new CmsUser { Id = id, UserName = email, Email = email, EmailConfirmed = true };
            Check(await users.CreateAsync(user), userName);
            Check(await users.AddToRoleAsync(user, stored.Id), userName);
        }

        // An admin user signs in to the admin, whose tokens carry the management scope.
        var principal = new Principal(PrincipalKind.AdminUser, id, userName, new Dictionary<string, string> { [TokenScopes.Claim] = TokenScopes.Management });
        return new TestUser(id, userName, email, stored.Name, stored.Id, principal);
    }

    public static async Task<TestApiKey> CreateApiKeyAsync(IServiceProvider services, ApiKeyClass keyClass, string role, string? name, CancellationToken cancellationToken)
    {
        var stored = await FindRoleAsync(services, role, cancellationToken);
        var keyName = name ?? $"{keyClass} key {Guid.CreateVersion7().ToString("N")[^6..]}";

        await using var scope = services.CreateAsyncScope();
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        CreatedApiKey created;
        using (SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id)))
        {
            created = await keys.CreateAsync(new ApiKeyRequest(keyName, ClassOf(keyClass), stored.Id), cancellationToken);
        }

        var key = await scope.ServiceProvider.GetRequiredService<ApiKeyPrincipals>().CurrentAsync(created.Key.Id, cancellationToken)
            ?? throw new InvalidOperationException($"The key '{keyName}' was not stored.");

        return new TestApiKey(created.Key.Id, keyName, keyClass, stored.Name, stored.Id, created.Secret, key);
    }

    private static void Check(IdentityResult result, string userName)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"The user '{userName}' could not be stored: {string.Join("; ", result.Errors.Select(error => $"{error.Code} {error.Description}"))}");
        }
    }

    private static Core.Identity.ApiKeys.ApiKeyClass ClassOf(ApiKeyClass keyClass) => keyClass switch
    {
        ApiKeyClass.Delivery => Core.Identity.ApiKeys.ApiKeyClass.Delivery,
        ApiKeyClass.Preview => Core.Identity.ApiKeys.ApiKeyClass.Preview,
        ApiKeyClass.Management => Core.Identity.ApiKeys.ApiKeyClass.Management,
        _ => throw new ArgumentOutOfRangeException(nameof(keyClass), keyClass, null),
    };
}
