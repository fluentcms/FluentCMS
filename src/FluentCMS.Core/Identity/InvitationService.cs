using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Providers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity;

internal sealed record InvitationSent(Guid Id, string Email, DateTimeOffset ExpiresAt);

internal sealed record PendingInvitation(Guid Id, string Email, DateTimeOffset ExpiresAt);

internal sealed record InvitationInfo(Guid Id, string Email, IReadOnlyList<Guid> RoleIds, DateTimeOffset CreatedAt, Guid CreatedBy, DateTimeOffset ExpiresAt);

// An invitation is a random token sent by email; the row keeps only its hash. Accepting it creates the user with the roles
// it names and uses it up, in one transaction. A new invitation to the same address ends the one before. Sending, listing
// and revoking need users.manage for the instance, since the user it becomes belongs to no site, and the sender must hold
// every grant of the roles it names.
internal sealed class InvitationService(
    SystemDbContext context,
    CmsUserManager users,
    IdentityChanges changes,
    IEmailSender email,
    IOperationContextAccessor accessor,
    ManagementAuthority authority,
    RoleService roleService,
    AdminUserOptions options,
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
                await Pending(normalized).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, now), cancellationToken);
                context.Add(invitation);
                await context.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);

        try
        {
            await email.SendAsync(Message(invitation, Link(acceptUrl, token)), cancellationToken);
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

    // False when no invitation with this id could still be accepted.
    public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await authority.RequireAsync(CorePermissions.UsersManage, site: null, cancellationToken);

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
            ? new PendingInvitation(invitation.Id, invitation.Email, invitation.ExpiresAt)
            : null;
    }

    public async Task<IdentityResult> AcceptAsync(string token, string userName, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);

        var hash = Hash(token);
        return await changes.RunAsync(
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

                // Deactivation revokes what the user sent; this holds if a revocation was missed.
                if (invitation.CreatedByKind == PrincipalKind.AdminUser
                    && !await context.Set<CmsUser>().AnyAsync(user => user.Id == invitation.CreatedBy && user.DeactivatedAt == null, cancellationToken))
                {
                    return Failed(IdentityErrorCodes.InvalidInvitation, "The invitation is not valid, or has been used.");
                }

                var user = new CmsUser { UserName = userName, Email = invitation.Email, EmailConfirmed = true, CreatedAt = now };
                var created = await users.CreateAsync(user, password);
                if (!created.Succeeded)
                {
                    return created;
                }

                // A role deleted since the invitation is not given.
                List<Guid> named = JsonSerializer.Deserialize<List<Guid>>(invitation.RoleIds) ?? [];
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

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private IQueryable<Invitation> Pending(string normalizedEmail) =>
        context.Set<Invitation>().Where(row => row.NormalizedEmail == normalizedEmail && row.AcceptedAt == null && row.RevokedAt == null);

    private static Uri Link(Uri acceptUrl, string token)
    {
        var builder = new UriBuilder(acceptUrl);
        var query = builder.Query.TrimStart('?');
        builder.Query = (query.Length == 0 ? "" : query + "&") + "token=" + Uri.EscapeDataString(token);
        return builder.Uri;
    }

    private static EmailMessage Message(Invitation invitation, Uri link) =>
        new(
            invitation.Email,
            "You are invited to FluentCMS",
            $"You have been invited to the FluentCMS admin. Accept the invitation and choose a password here:\n\n{link.AbsoluteUri}\n\nThe link can be used once and expires at {invitation.ExpiresAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC.");

    private static IdentityResult Failed(string code, string description) =>
        IdentityResult.Failed(new IdentityError { Code = code, Description = description });

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
