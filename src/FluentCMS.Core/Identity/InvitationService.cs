using System.Collections.Immutable;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Providers;
using FluentCMS.Sites;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace FluentCMS.Core.Identity;

internal sealed record InvitationSent(Guid Id, string Email, DateTimeOffset ExpiresAt);

internal sealed record PendingInvitation(Guid Id, string Email, DateTimeOffset ExpiresAt);

internal sealed record InvitationInfo(Guid Id, string Email, IReadOnlyList<Guid> RoleIds, DateTimeOffset CreatedAt, Guid CreatedBy, DateTimeOffset ExpiresAt);

// An invitation is a random token sent by email; the row keeps only its hash. Accepting it creates the user with the roles
// it names and uses it up, in one transaction, with a password or, through a provider whose address for the person is the
// invitation's, with that external login; with cms.Sso.DisableLocalLogin only the latter. A new invitation to the same
// address ends the one before. Sending, listing and revoking need users.manage for the instance, since the user it becomes
// belongs to no site, and the sender must hold every grant of the roles it names; a new invitation that ends pending ones
// needs every grant of their roles too, as revoking them would.
internal sealed class InvitationService(
    SystemDbContext context,
    CmsUserManager users,
    IdentityChanges changes,
    IEmailSender email,
    IOperationContextAccessor accessor,
    ManagementAuthority authority,
    RoleService roleService,
    ApiKeyPrincipals keys,
    AdminUserOptions options,
    SsoOptions sso,
    TimeProvider time)
{
    private const int TokenBytes = 32;

    public async Task<InvitationSent> InviteAsync(string address, IReadOnlyCollection<Guid> roleIds, Uri acceptUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(roleIds);
        ArgumentNullException.ThrowIfNull(acceptUrl);

        if (!acceptUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("The page that accepts an invitation is linked from an email, so its address is absolute.", nameof(acceptUrl));
        }

        await authority.RequireAsync(CorePermissions.UsersManage, site: null, cancellationToken);

        var trimmed = address.Trim();
        if (!MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed)
        {
            throw new ValidationException([new ValidationError("email", IdentityErrorCodes.InvalidEmail, "The address is not a valid email address.")]);
        }

        List<Guid> roles = [.. roleIds.Distinct()];
        var found = await context.Set<Role>().AsNoTracking().CountAsync(role => roles.Contains(role.Id), cancellationToken);
        if (found != roles.Count)
        {
            throw new ValidationException([new ValidationError("roles", IdentityErrorCodes.UnknownRole, "An invitation names only roles that exist.")]);
        }

        await roleService.RequireAssignableAsync(roles, cancellationToken);

        var normalized = users.NormalizeEmail(trimmed) ?? trimmed;
        if (await users.FindByEmailAsync(trimmed) is not null)
        {
            throw new ValidationException([new ValidationError("email", IdentityErrorCodes.EmailTaken, "A user has this email address.")]);
        }

        var token = Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = time.GetUtcNow();
        var inviter = accessor.Current?.AuditPrincipal ?? Principal.System;
        var invitation = new Invitation
        {
            Id = Guid.CreateVersion7(),
            Email = trimmed,
            NormalizedEmail = normalized,
            TokenHash = Hash(token),
            RoleIds = JsonSerializer.Serialize(roles),
            CreatedAt = now,
            CreatedBy = inviter.Id,
            CreatedByKind = inviter.Kind,
            ExpiresAt = now + options.InvitationLifetime,
        };

        await changes.RunAsync(
            async () =>
            {
                // Only the ones that could still be accepted hold the caller to their roles, and exactly those are ended; an
                // expired one, which the list does not show, is ended unchecked. The expiry is compared here, as in the list.
                var pending = await Pending(normalized).AsNoTracking().Select(row => new { row.Id, row.RoleIds, row.ExpiresAt }).ToListAsync(cancellationToken);
                var live = pending.Where(row => row.ExpiresAt > now).ToList();
                await roleService.RequireAssignableAsync([.. live.SelectMany(row => JsonSerializer.Deserialize<List<Guid>>(row.RoleIds) ?? [])], cancellationToken);
                List<Guid> ended = [.. pending.Select(row => row.Id)];
                await Pending(normalized).Where(row => ended.Contains(row.Id)).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, now), cancellationToken);
                context.Add(invitation);
                await context.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);

        try
        {
            await email.SendAsync(Message(invitation, EmailLinks.With(acceptUrl, ("token", token))), cancellationToken);
        }
        catch
        {
            // A link that never arrived must not stay usable.
            await context.Set<Invitation>().Where(row => row.Id == invitation.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, time.GetUtcNow()), CancellationToken.None);
            throw;
        }

        return new InvitationSent(invitation.Id, invitation.Email, invitation.ExpiresAt);
    }

    // The invitations that could still be accepted, the newest first.
    public async Task<IReadOnlyList<InvitationInfo>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        await authority.RequireAsync(CorePermissions.UsersManage, site: null, cancellationToken);

        var now = time.GetUtcNow();
        var rows = await context.Set<Invitation>().AsNoTracking()
            .Where(row => row.AcceptedAt == null && row.RevokedAt == null)
            .ToListAsync(cancellationToken);

        return [.. rows.Where(row => row.ExpiresAt > now)
            .OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Select(row => new InvitationInfo(row.Id, row.Email, JsonSerializer.Deserialize<List<Guid>>(row.RoleIds) ?? [], row.CreatedAt, row.CreatedBy, row.ExpiresAt))];
    }

    // False when no invitation with this id could still be accepted. Revoking one needs what sending it would, so a caller
    // below its roles does not cancel it.
    public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await authority.RequireAsync(CorePermissions.UsersManage, site: null, cancellationToken);

        var pending = await context.Set<Invitation>().AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id && row.AcceptedAt == null && row.RevokedAt == null, cancellationToken);
        if (pending is null)
        {
            return false;
        }

        await roleService.RequireAssignableAsync(JsonSerializer.Deserialize<List<Guid>>(pending.RoleIds) ?? [], cancellationToken);

        var now = time.GetUtcNow();
        var revoked = await context.Set<Invitation>()
            .Where(row => row.Id == id && row.AcceptedAt == null && row.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, now), cancellationToken);
        return revoked == 1;
    }

    // For the page that accepts it: whether the token is one that would be accepted now.
    public async Task<PendingInvitation?> FindAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var hash = Hash(token);
        var invitation = await context.Set<Invitation>().AsNoTracking().FirstOrDefaultAsync(row => row.TokenHash == hash, cancellationToken);
        return invitation is { AcceptedAt: null, RevokedAt: null } && invitation.ExpiresAt > time.GetUtcNow()
            && await InviterStandsAsync(invitation, JsonSerializer.Deserialize<List<Guid>>(invitation.RoleIds) ?? [], cancellationToken)
                ? new PendingInvitation(invitation.Id, invitation.Email, invitation.ExpiresAt)
                : null;
    }

    public async Task<IdentityResult> AcceptAsync(string token, string userName, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);

        if (sso.DisableLocalLogin)
        {
            return Failed(IdentityErrorCodes.LocalLoginDisabled, "Sign-in with a password is turned off here. Accept the invitation with a provider.");
        }

        return await AcceptAsync(token, userName, address: null, user => users.CreateAsync(user, password), cancellationToken);
    }

    // The user is named by the invitation's address and has no password. The address the provider gave for the person must
    // be the invitation's; whether the provider vouches for it is the caller's to check.
    public async Task<IdentityResult> AcceptAsync(string token, string address, UserLoginInfo login, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(login);

        return await AcceptAsync(
            token,
            userName: null,
            address,
            async user => await users.CreateAsync(user) is { Succeeded: false } created ? created : await users.AddLoginAsync(user, login),
            cancellationToken);
    }

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<IdentityResult> AcceptAsync(string token, string? userName, string? address, Func<CmsUser, Task<IdentityResult>> create, CancellationToken cancellationToken)
    {
        var hash = Hash(token);
        return await changes.RunNamingAsync(
            async () =>
            {
                var now = time.GetUtcNow();
                var invitation = await context.Set<Invitation>().AsNoTracking().FirstOrDefaultAsync(row => row.TokenHash == hash, cancellationToken);
                if (invitation is not { AcceptedAt: null, RevokedAt: null })
                {
                    return Failed(IdentityErrorCodes.InvalidInvitation, "The invitation is not valid, or has been used.");
                }

                if (invitation.ExpiresAt <= now)
                {
                    return Failed(IdentityErrorCodes.InvitationExpired, "The invitation has expired. Ask for a new one.");
                }

                List<Guid> named = JsonSerializer.Deserialize<List<Guid>>(invitation.RoleIds) ?? [];
                if (!await InviterStandsAsync(invitation, named, cancellationToken))
                {
                    return Failed(IdentityErrorCodes.InvalidInvitation, "The invitation is not valid, or has been used.");
                }

                if (address is not null && users.NormalizeEmail(address.Trim()) != invitation.NormalizedEmail)
                {
                    return Failed(IdentityErrorCodes.InvitationLoginRefused, "The address the provider gave for you is not the one this invitation was sent to.");
                }

                // A name that is an address is the invitation's own: any other gets one answer, whether or not it is a user's,
                // so that the page tells no invitee whose addresses the users have (#3373).
                if (userName is not null && userName.Contains('@', StringComparison.Ordinal) && users.NormalizeEmail(userName) != invitation.NormalizedEmail)
                {
                    return Failed(nameof(IdentityErrorDescriber.InvalidUserName), "A user name that is an email address must be the address this invitation was sent to.");
                }

                var user = new CmsUser { UserName = userName ?? invitation.Email, Email = invitation.Email, EmailConfirmed = true, CreatedAt = now };
                var created = await create(user);
                if (!created.Succeeded)
                {
                    return created;
                }

                // A role deleted since the invitation is not given.
                List<Guid> roles = await context.Set<Role>().AsNoTracking().Where(role => named.Contains(role.Id)).Select(role => role.Id).ToListAsync(cancellationToken);
                if (roles.Count > 0 && await users.AddToRolesAsync(user, roles) is { Succeeded: false } assigned)
                {
                    return assigned;
                }

                // Two accepts of one token race here, and one of them finds nothing left to claim.
                var claimed = await context.Set<Invitation>()
                    .Where(row => row.Id == invitation.Id && row.AcceptedAt == null && row.RevokedAt == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AcceptedAt, now).SetProperty(row => row.UserId, user.Id), cancellationToken);

                return claimed == 1 ? IdentityResult.Success : Failed(IdentityErrorCodes.InvalidInvitation, "The invitation is not valid, or has been used.");
            },
            cancellationToken);
    }

    // An invitation is only as good as the credential that sent it: the user still active, the key not revoked or expired,
    // the service account's client still configured, and each of them still holding users.manage for the instance and every
    // grant of the roles it names, as its roles are now. Deactivating a user or revoking a key also revokes what it sent; this holds when that was missed and
    // when the inviter was only demoted. Only the system, the command line tool, is not checked again; any other kind fails.
    private async Task<bool> InviterStandsAsync(Invitation invitation, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (invitation.CreatedByKind == PrincipalKind.System && invitation.CreatedBy == Principal.System.Id)
        {
            return true;
        }

        var inviter = await InviterAsync(invitation, cancellationToken);
        if (inviter is null)
        {
            return false;
        }

        var previous = accessor.Current;
        accessor.Current = new OperationContext(inviter, Origin.Api, previous?.Site ?? new SiteId(DefaultSite.Id), previous?.Locale ?? DefaultSite.Locale, null, previous?.CorrelationId ?? Guid.CreateVersion7().ToString(), time.GetUtcNow());
        try
        {
            if (!await authority.AllowsAsync(CorePermissions.UsersManage, site: null, cancellationToken))
            {
                return false;
            }

            await roleService.RequireAssignableAsync(roleIds, cancellationToken);
            return true;
        }
        catch (ManagementAccessDeniedException)
        {
            return false;
        }
        finally
        {
            accessor.Current = previous;
        }
    }

    // The inviter as a principal of the management scope, whose roles the evaluator reads as they are now; null when it is gone.
    private async Task<Principal?> InviterAsync(Invitation invitation, CancellationToken cancellationToken)
    {
        Dictionary<string, string> management = new() { [TokenScopes.Claim] = TokenScopes.Management };
        switch (invitation.CreatedByKind)
        {
            case PrincipalKind.AdminUser:
                return await context.Set<CmsUser>().AsNoTracking().FirstOrDefaultAsync(user => user.Id == invitation.CreatedBy && user.DeactivatedAt == null, cancellationToken) is { } user
                    ? new Principal(PrincipalKind.AdminUser, user.Id, user.UserName ?? "", management)
                    : null;
            case PrincipalKind.ApiKey:
                return await keys.ActiveAsync(invitation.CreatedBy, cancellationToken) is { } key ? ApiKeyClaims.PrincipalOf(key) : null;
            case PrincipalKind.ServiceAccount:
                if (await ServiceAccountRolesAsync(invitation.CreatedBy, cancellationToken) is not { } roles)
                {
                    return null;
                }

                management[CmsClaimTypes.Roles] = string.Join(' ', roles.Select(role => role.ToString("D")));
                return new Principal(PrincipalKind.ServiceAccount, invitation.CreatedBy, "", management);
            default:
                return null;
        }
    }

    // The roles the token endpoint would give the client now: the instance roles its configuration names.
    private async Task<IReadOnlyList<Guid>?> ServiceAccountRolesAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await context.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>().AsNoTracking()
            .Where(row => row.Id == applicationId)
            .Select(row => new { row.Properties })
            .FirstOrDefaultAsync(cancellationToken);
        if (application is null)
        {
            return null;
        }

        var properties = string.IsNullOrEmpty(application.Properties)
            ? ImmutableDictionary<string, JsonElement>.Empty
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(application.Properties)?.ToImmutableDictionary() ?? ImmutableDictionary<string, JsonElement>.Empty;
        if (MemberConnect.IsMemberClient(properties)
            || !properties.TryGetValue(AuthorizationServer.RolesProperty, out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> names = [.. element.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)];
        return await context.Set<Role>().AsNoTracking().Where(role => role.SiteId == null && names.Contains(role.Name)).Select(role => role.Id).ToListAsync(cancellationToken);
    }

    private IQueryable<Invitation> Pending(string normalizedEmail) =>
        context.Set<Invitation>().Where(row => row.NormalizedEmail == normalizedEmail && row.AcceptedAt == null && row.RevokedAt == null);

    private EmailMessage Message(Invitation invitation, Uri link)
    {
        var how = sso.DisableLocalLogin ? "by signing in with your organization's account"
            : sso.Providers.Count > 0 ? "by choosing a password or signing in with your organization's account"
            : "and choose a password";
        return new(
            invitation.Email,
            "You are invited to FluentCMS",
            $"You have been invited to the FluentCMS admin. Accept the invitation {how} here:\n\n{link.AbsoluteUri}\n\nThe link can be used once and expires at {invitation.ExpiresAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC.");
    }

    private static IdentityResult Failed(string code, string description) =>
        IdentityResult.Failed(new IdentityError { Code = code, Description = description });

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
