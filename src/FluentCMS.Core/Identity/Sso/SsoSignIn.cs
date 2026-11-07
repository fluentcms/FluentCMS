using System.Net.Mail;
using System.Security.Claims;
using FluentCMS.Core.Authorization;
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
// provider is refused rather than taken over, unless the provider links existing users and the token says the address is
// verified. An invitation is accepted through a provider whose address for the person is the invitation's. The provider's
// amr=mfa counts as the second factor only when the provider is trusted for it; otherwise a role that requires two-factor
// asks for the local one after the provider. Groups that would take Super Admin from its last active holder change no role
// and refuse the sign-in, since the provider no longer vouches for it.
internal sealed class SsoSignIn(
    SsoOptions options,
    CmsUserManager users,
    CmsSignInManager signIn,
    SignInEvents events,
    IdentityChanges changes,
    SuperAdminGuard superAdmins,
    InvitationService invitations,
    SystemDbContext context,
    ILogger<SsoSignIn> logger)
{
    private const string EmailVerifiedClaim = "email_verified";

    public async Task<SignInResult> SignInAsync(ExternalLoginInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (!options.Providers.TryGetValue(info.LoginProvider, out var provider))
        {
            return await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false);
        }

        var user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user is null)
        {
            var existing = Email(info, provider) is { } email ? await users.FindByEmailAsync(email) : null;
            if (existing is not null && provider.LinkExistingUsers)
            {
                user = await LinkAsync(existing, info, cancellationToken);
                if (user is null)
                {
                    return SignInResult.NotAllowed;
                }
            }
            else if (provider.AutoProvision)
            {
                return await ProvisionAsync(info, provider, existing, cancellationToken) is null ? SignInResult.NotAllowed : await ExternalSignInAsync(info, provider);
            }
        }

        if (user is { DeactivatedAt: null } && !await users.IsLockedOutAsync(user) && !await FollowGroupsAsync(user, info, provider, cancellationToken))
        {
            await events.FailedAsync(signIn.Context, user, LoginMethods.External, LoginFailureReasons.NotAllowed);
            return SignInResult.NotAllowed;
        }

        return await ExternalSignInAsync(info, provider);
    }

    // The invitation becomes a user linked to the external login, with the invitation's roles, who then signs in as at any
    // sign-in through the provider. The provider's address must be the invitation's and not one it says it has not
    // verified; the link of the email already showed that the person reads that mailbox.
    public async Task<(IdentityResult Accepted, SignInResult SignedIn)> AcceptInvitationAsync(ExternalLoginInfo info, string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(token);

        if (!options.Providers.TryGetValue(info.LoginProvider, out var provider))
        {
            return (await RefusedLinkAsync(info, IdentityErrorCodes.InvitationLoginRefused, "the provider is not one of cms.Sso.", "Only a single sign-on provider of this instance accepts an invitation."), SignInResult.Failed);
        }

        if (Email(info, provider) is not { } email)
        {
            return (await RefusedLinkAsync(info, IdentityErrorCodes.SsoNoEmail, $"the token has no email address in the claim '{provider.EmailClaim}'.", "The provider gave no email address for you."), SignInResult.Failed);
        }

        if (string.Equals(info.Principal.FindFirstValue(EmailVerifiedClaim), "false", StringComparison.OrdinalIgnoreCase))
        {
            return (await RefusedLinkAsync(info, IdentityErrorCodes.SsoNoEmail, "the provider says the email address is not verified.", "The provider says your email address is not verified."), SignInResult.Failed);
        }

        var accepted = await invitations.AcceptAsync(token, email, new UserLoginInfo(info.LoginProvider, info.ProviderKey, info.ProviderDisplayName), cancellationToken);
        if (!accepted.Succeeded)
        {
            SsoLog.LinkRefused(logger, IdentityErrorCodes.InvitationLoginRefused, info.LoginProvider, "the invitation was not accepted: " + string.Join(", ", accepted.Errors.Select(error => error.Code)));
            await events.FailedAsync(signIn.Context, null, LoginMethods.External, LoginFailureReasons.NotAllowed);
            return (accepted, SignInResult.Failed);
        }

        if (await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey) is { } user)
        {
            SsoLog.InvitationAccepted(logger, user.Id, info.LoginProvider);
        }

        return (accepted, await SignInAsync(info, cancellationToken));
    }

    private Task<SignInResult> ExternalSignInAsync(ExternalLoginInfo info, SsoProvider provider) =>
        signIn.ExternalSignInAsync(info.LoginProvider, info.ProviderKey, multiFactor: provider.TrustMfaClaim && info.Principal.HasClaim(Claims.AuthenticationMethodReference, TokenClaims.MultiFactor));

    // The plain address of the provider's EmailClaim, or null.
    private static string? Email(ExternalLoginInfo info, SsoProvider provider)
    {
        var email = info.Principal.FindFirstValue(provider.EmailClaim)?.Trim();
        return !string.IsNullOrEmpty(email) && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email ? email : null;
    }

    // Only for a token that says the address is verified, and a user who may sign in now and has no other login of the
    // provider, so that a second account at the provider does not join the first.
    private async Task<CmsUser?> LinkAsync(CmsUser user, ExternalLoginInfo info, CancellationToken cancellationToken)
    {
        if (!string.Equals(info.Principal.FindFirstValue(EmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase))
        {
            await RefusedLinkAsync(info, IdentityErrorCodes.SsoNoEmail, "the token does not say the email address is verified, so the user it belongs to is not linked.", null);
            return null;
        }

        if (!await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
        {
            await RefusedLinkAsync(info, IdentityErrorCodes.SsoEmailTaken, "the email address belongs to a user who cannot sign in now.", null);
            return null;
        }

        if ((await users.GetLoginsAsync(user)).Any(login => string.Equals(login.LoginProvider, info.LoginProvider, StringComparison.OrdinalIgnoreCase)))
        {
            await RefusedLinkAsync(info, IdentityErrorCodes.SsoEmailTaken, "the email address belongs to a user linked to another account of this provider.", null);
            return null;
        }

        var linked = await changes.RunAsync(() => users.AddLoginAsync(user, new UserLoginInfo(info.LoginProvider, info.ProviderKey, info.ProviderDisplayName)), cancellationToken);
        if (!linked.Succeeded)
        {
            await RefusedLinkAsync(info, IdentityErrorCodes.SsoProvisioningFailed, string.Join(", ", linked.Errors.Select(error => error.Code)), null);
            return null;
        }

        SsoLog.UserLinked(logger, user.Id, info.LoginProvider);
        return user;
    }

    // Existing is the user the token's address belongs to, if any.
    private async Task<CmsUser?> ProvisionAsync(ExternalLoginInfo info, SsoProvider provider, CmsUser? existing, CancellationToken cancellationToken)
    {
        if (Email(info, provider) is not { } email)
        {
            return await RefusedAsync(info, IdentityErrorCodes.SsoNoEmail, $"the token has no email address in the claim '{provider.EmailClaim}'.");
        }

        if (info.Principal.FindFirstValue(EmailVerifiedClaim) is "false")
        {
            return await RefusedAsync(info, IdentityErrorCodes.SsoNoEmail, "the provider says the email address is not verified.");
        }

        if (existing is not null)
        {
            return await RefusedAsync(info, IdentityErrorCodes.SsoEmailTaken, "the email address belongs to a user who is not linked to this provider.");
        }

        var roles = await RolesAsync(info, provider, cancellationToken);
        List<Guid> given = [.. roles.Mapped];
        if (roles.Default is { } role && !given.Contains(role))
        {
            given.Add(role);
        }

        var user = new CmsUser { UserName = email, Email = email, EmailConfirmed = true };
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
    // the provider does not map stay as they are. False when that would take Super Admin from its last active holder.
    private async Task<bool> FollowGroupsAsync(CmsUser user, ExternalLoginInfo info, SsoProvider provider, CancellationToken cancellationToken)
    {
        if (provider.RoleMappings.Count == 0)
        {
            return true;
        }

        var roles = await RolesAsync(info, provider, cancellationToken);
        var held = await users.GetRoleIdsAsync(user);
        List<Guid> added = [.. roles.Mapped.Where(role => !held.Contains(role))];
        List<Guid> removed = [.. roles.Governed.Where(role => role != roles.Default && !roles.Mapped.Contains(role) && held.Contains(role))];
        if (added.Count == 0 && removed.Count == 0)
        {
            return true;
        }

        async Task<IdentityResult> FollowAsync()
        {
            if (added.Count > 0 && await users.AddToRolesAsync(user, added) is { Succeeded: false } failed)
            {
                return failed;
            }

            return removed.Count > 0 ? await users.RemoveFromRolesAsync(user, removed) : IdentityResult.Success;
        }

        var result = removed.Count > 0
            ? await superAdmins.ChangeUserAsync(user, "follow_groups", FollowAsync, cancellationToken)
            : await changes.RunAsync(FollowAsync, cancellationToken);

        if (result.Errors.Any(error => error.Code == IdentityErrorCodes.LastSuperAdmin))
        {
            SsoLog.LastSuperAdminKept(logger, IdentityErrorCodes.LastSuperAdmin, info.LoginProvider, user.Id);
            return false;
        }

        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{IdentityErrorCodes.SsoProvisioningFailed}: the roles of user {user.Id:D} could not follow the groups of the provider {info.LoginProvider}: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
        }

        return true;
    }

    // The roles the provider names that exist: a role id, or the exact name of an instance-wide role. Mapped are the roles of
    // the token's groups, Governed every role a mapping names.
    private async Task<(Guid? Default, HashSet<Guid> Mapped, HashSet<Guid> Governed)> RolesAsync(ExternalLoginInfo info, SsoProvider provider, CancellationToken cancellationToken)
    {
        List<string> references = [.. provider.RoleMappings.Select(mapping => mapping.Role.Trim())];
        if (!string.IsNullOrWhiteSpace(provider.DefaultRole))
        {
            references.Add(provider.DefaultRole.Trim());
        }

        List<Guid> ids = [.. references.Select(reference => Guid.TryParse(reference, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty)];
        var known = await context.Set<Role>().AsNoTracking()
            .Where(role => ids.Contains(role.Id) || (role.SiteId == null && references.Contains(role.Name)))
            .Select(role => new { role.Id, role.Name, role.SiteId })
            .ToListAsync(cancellationToken);

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
        if (groups.Count == 0 && provider.RoleMappings.Count > 0)
        {
            // Fails closed: the mapped roles go. Said in the log, since a provider that leaves the claim out by mistake
            // (Entra ID's group overage, a missing scope or mapper) looks the same.
            SsoLog.NoGroups(logger, info.LoginProvider, provider.GroupsClaim);
        }
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

    // The reason goes to the log; the description is what a page shows.
    private async Task<IdentityResult> RefusedLinkAsync(ExternalLoginInfo info, string code, string reason, string? description)
    {
        SsoLog.LinkRefused(logger, code, info.LoginProvider, reason);
        await events.FailedAsync(signIn.Context, null, LoginMethods.External, LoginFailureReasons.NotAllowed);
        return IdentityResult.Failed(new IdentityError { Code = code, Description = description ?? reason });
    }
}
