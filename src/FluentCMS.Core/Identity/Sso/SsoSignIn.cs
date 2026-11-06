using System.Net.Mail;
using System.Security.Claims;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Sso;

// The sign-in of the external login page. A user linked to the external login signs in. Through a provider of cms.Sso,
// someone with no user yet gets one, linked in cms_user_login, with the provider's default role and mapped roles; at every
// sign-in after that the mapped roles follow the token's groups. An email address that belongs to a user not linked to the
// provider is refused rather than taken over. The provider's amr=mfa counts as the second factor only when the provider is
// trusted for it; otherwise a role that requires two-factor asks for the local one after the provider.
internal sealed class SsoSignIn(
    SsoOptions options,
    CmsUserManager users,
    CmsSignInManager signIn,
    SignInEvents events,
    IdentityChanges changes,
    SystemDbContext context,
    ILogger<SsoSignIn> logger)
{
    public async Task<SignInResult> SignInAsync(ExternalLoginInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (!options.Providers.TryGetValue(info.LoginProvider, out var provider))
        {
            return await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false);
        }

        var user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user is null && provider.AutoProvision)
        {
            user = await ProvisionAsync(info, provider, cancellationToken);
            if (user is null)
            {
                return SignInResult.NotAllowed;
            }
        }
        else if (user is { DeactivatedAt: null })
        {
            await FollowGroupsAsync(user, info, provider, cancellationToken);
        }

        return await signIn.ExternalSignInAsync(info.LoginProvider, info.ProviderKey, multiFactor: provider.TrustMfaClaim && info.Principal.HasClaim(Claims.AuthenticationMethodReference, TokenClaims.MultiFactor));
    }

    private async Task<CmsUser?> ProvisionAsync(ExternalLoginInfo info, SsoProvider provider, CancellationToken cancellationToken)
    {
        var email = info.Principal.FindFirstValue(provider.EmailClaim)?.Trim();
        if (string.IsNullOrEmpty(email) || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
        {
            return await RefusedAsync(info, IdentityErrorCodes.SsoNoEmail, $"the token has no email address in the claim '{provider.EmailClaim}'.");
        }

        if (await users.FindByEmailAsync(email) is not null)
        {
            return await RefusedAsync(info, IdentityErrorCodes.SsoEmailTaken, "the email address belongs to a user who is not linked to this provider.");
        }

        var roles = await RolesAsync(info, provider, cancellationToken);
        List<Guid> given = [.. roles.Mapped];
        if (roles.Default is { } role && !given.Contains(role))
        {
            given.Add(role);
        }

        var user = new CmsUser { UserName = email, Email = email, EmailConfirmed = info.Principal.FindFirstValue("email_verified") is not "false" };
        var result = await changes.RunAsync(
            async () =>
            {
                if (await users.CreateAsync(user) is { Succeeded: false } created)
                {
                    return created;
                }

                if (await users.AddLoginAsync(user, new UserLoginInfo(info.LoginProvider, info.ProviderKey, info.ProviderDisplayName)) is { Succeeded: false } linked)
                {
                    return linked;
                }

                return given.Count > 0 ? await users.AddToRolesAsync(user, given) : IdentityResult.Success;
            },
            cancellationToken);

        if (!result.Succeeded)
        {
            // The codes only: a description can hold the address.
            return await RefusedAsync(info, IdentityErrorCodes.SsoProvisioningFailed, string.Join(", ", result.Errors.Select(error => error.Code)));
        }

        SsoLog.UserProvisioned(logger, user.Id, info.LoginProvider);
        return user;
    }

    // Adds the roles mapped to the token's groups and takes away the other roles the provider maps; the default role and roles
    // the provider does not map stay as they are.
    private async Task FollowGroupsAsync(CmsUser user, ExternalLoginInfo info, SsoProvider provider, CancellationToken cancellationToken)
    {
        if (provider.RoleMappings.Count == 0)
        {
            return;
        }

        var roles = await RolesAsync(info, provider, cancellationToken);
        var held = await users.GetRoleIdsAsync(user);
        List<Guid> added = [.. roles.Mapped.Where(role => !held.Contains(role))];
        List<Guid> removed = [.. roles.Governed.Where(role => role != roles.Default && !roles.Mapped.Contains(role) && held.Contains(role))];
        if (added.Count == 0 && removed.Count == 0)
        {
            return;
        }

        var result = await changes.RunAsync(
            async () =>
            {
                if (added.Count > 0 && await users.AddToRolesAsync(user, added) is { Succeeded: false } failed)
                {
                    return failed;
                }

                return removed.Count > 0 ? await users.RemoveFromRolesAsync(user, removed) : IdentityResult.Success;
            },
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{IdentityErrorCodes.SsoProvisioningFailed}: the roles of user {user.Id:D} could not follow the groups of the provider {info.LoginProvider}: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
        }
    }

    // The roles the provider names that exist: a role id, or the exact name of an instance-wide role. Mapped are the roles of
    // the token's groups, Governed every role a mapping names.
    private async Task<(Guid? Default, HashSet<Guid> Mapped, HashSet<Guid> Governed)> RolesAsync(ExternalLoginInfo info, SsoProvider provider, CancellationToken cancellationToken)
    {
        var known = await context.Set<Role>().AsNoTracking().Select(role => new { role.Id, role.Name, role.SiteId }).ToListAsync(cancellationToken);

        Guid? Find(string reference)
        {
            var trimmed = reference.Trim();
            var found = Guid.TryParse(trimmed, out var id)
                ? known.Find(role => role.Id == id)
                : known.Find(role => role.SiteId == null && string.Equals(role.Name, trimmed, StringComparison.Ordinal));
            if (found is null)
            {
                SsoLog.RoleNotFound(logger, IdentityErrorCodes.SsoRoleNotFound, info.LoginProvider, trimmed);
            }

            return found?.Id;
        }

        var groups = info.Principal.FindAll(provider.GroupsClaim).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);
        HashSet<Guid> mapped = [];
        HashSet<Guid> governed = [];
        foreach (var mapping in provider.RoleMappings)
        {
            if (Find(mapping.Role) is { } role)
            {
                governed.Add(role);
                if (groups.Contains(mapping.Group.Trim()))
                {
                    mapped.Add(role);
                }
            }
        }

        return (string.IsNullOrWhiteSpace(provider.DefaultRole) ? null : Find(provider.DefaultRole), mapped, governed);
    }

    private async Task<CmsUser?> RefusedAsync(ExternalLoginInfo info, string code, string reason)
    {
        SsoLog.ProvisioningRefused(logger, code, info.LoginProvider, reason);
        await events.FailedAsync(signIn.Context, null, LoginMethods.External, LoginFailureReasons.NotAllowed);
        return null;
    }
}
