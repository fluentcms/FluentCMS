using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity.Members;

// Which members a list holds; a null member of it does not narrow the list.
internal sealed record MemberFilter(string? Search = null, Guid? GroupId = null, bool? Verified = null, bool? Active = null);

internal sealed record MemberPage(IReadOnlyList<MemberInfo> Items, long Total, int Page, int PageSize);

// Members for the Management API. A member belongs to no site, so everything here needs members.manage for the
// instance, which is apart from users.manage: who looks after the visitors need not look after the administrators.
// Putting a member in a group hands it the grants of the group's role, so that also needs every grant of that role.
// Deactivating or deleting a member ends its sessions, so its access tokens stop at once.
internal sealed class MemberAdministration(
    MemberManager members,
    SystemDbContext context,
    ManagementAuthority authority,
    MemberGroupService groups,
    IdentityChanges changes,
    MemberSessions sessions,
    MemberLogins logins,
    MemberMail mail)
{
    public async Task<MemberPage> ListAsync(MemberFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await RequireAsync(cancellationToken);

        var query = context.Set<CmsMember>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search) && members.NormalizeEmail(filter.Search.Trim()) is { } term)
        {
            query = query.Where(member => member.NormalizedEmail!.Contains(term));
        }

        if (filter.GroupId is { } groupId)
        {
            query = query.Where(member => context.Set<MemberGroupMember>().Any(membership => membership.MemberId == member.Id && membership.GroupId == groupId));
        }

        if (filter.Verified is { } verified)
        {
            query = query.Where(member => member.EmailConfirmed == verified);
        }

        if (filter.Active is { } active)
        {
            query = active ? query.Where(member => member.DeactivatedAt == null) : query.Where(member => member.DeactivatedAt != null);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var found = await query.OrderBy(member => member.CreatedAt).ThenBy(member => member.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        var held = await MemberViews.GroupsOfAsync(context, [.. found.Select(member => member.Id)], cancellationToken);

        return new MemberPage([.. found.Select(member => MemberViews.Info(member, held[member.Id]))], total, page, pageSize);
    }

    public async Task<MemberInfo> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        return await InfoAsync(await FindAsync(id), cancellationToken);
    }

    // The member has no password and an address that is not verified; the mailed link sets the one and verifies the
    // other. Without anywhere for the link to go the member could never sign in, so it is not made.
    public async Task<MemberInfo> CreateAsync(string email, string? displayName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        await RequireAsync(cancellationToken);
        if (!mail.CanLinkToPasswordReset)
        {
            throw new ValidationException([new ValidationError("", MemberErrorCodes.NoMailLink, "A new member gets a link to choose its password, and the link has nowhere to go. Set FluentCms:PublicOrigin, or FluentCms:Members:Links:ResetPassword to the page of the front end.")]);
        }

        var member = new CmsMember { Email = email.Trim(), DisplayName = MemberViews.DisplayName(displayName) };
        MemberViews.Check(await members.CreateAsync(member));
        await mail.SendSetPasswordAsync(member, await members.GeneratePasswordResetTokenAsync(member), cancellationToken);
        return await InfoAsync(member, cancellationToken);
    }

    public async Task<MemberInfo> UpdateAsync(Guid id, string? displayName, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var member = await FindAsync(id);
        member.DisplayName = MemberViews.DisplayName(displayName);
        MemberViews.Check(await members.UpdateAsync(member));
        return await InfoAsync(member, cancellationToken);
    }

    public async Task<MemberInfo> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var member = await FindAsync(id);
        MemberViews.Check(await members.DeactivateAsync(member));
        await sessions.RevokeAllAsync(member.Id, cancellationToken);
        return await InfoAsync(member, cancellationToken);
    }

    public async Task<MemberInfo> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var member = await FindAsync(id);
        MemberViews.Check(await members.ReactivateAsync(member));
        return await InfoAsync(member, cancellationToken);
    }

    // The member goes first: from then on it gets no new token, and the tokens it has are ended next.
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var member = await FindAsync(id);
        MemberViews.Check(await members.DeleteAsync(member));
        await sessions.RevokeAllAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<UserSession>> SessionsAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var member = await FindAsync(id);
        return await sessions.ListAsync(member.Id, cancellationToken);
    }

    public async Task RevokeSessionAsync(Guid id, Guid sessionId, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var member = await FindAsync(id);
        if (!await sessions.RevokeAsync(member.Id, sessionId, cancellationToken))
        {
            throw UserViews.SessionNotFound(sessionId);
        }
    }

    public async Task<IReadOnlyList<MemberLogin>> LoginsAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        return await logins.ListAsync(await FindAsync(id));
    }

    public async Task RemoveLoginAsync(Guid id, string provider, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        await logins.RemoveAsync(await FindAsync(id), provider, cancellationToken);
    }

    public async Task<MemberInfo> AddToGroupAsync(Guid id, Guid groupId, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var member = await FindAsync(id);
        var group = await groups.RequireAssignableAsync(groupId, cancellationToken);

        try
        {
            await changes.RunAsync(
                async () =>
                {
                    if (await context.Set<MemberGroupMember>().AnyAsync(membership => membership.MemberId == id && membership.GroupId == groupId, cancellationToken))
                    {
                        throw AlreadyInGroup(group);
                    }

                    context.Add(new MemberGroupMember { MemberId = id, GroupId = groupId });
                    await context.SaveChangesAsync(cancellationToken);
                    await changes.RaiseAsync(new MemberGroupChangedEvent(id, member.Email ?? "", [groupId], []), cancellationToken);
                    return true;
                },
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Put in the group by another call at the same time, or the member or the group went meanwhile.
            await FindAsync(id);
            await groups.FindAsync(groupId, cancellationToken);
            throw AlreadyInGroup(group);
        }

        return await InfoAsync(member, cancellationToken);
    }

    // Taking a member out of a group needs the grants of its role as putting one in does: a row rule that negates the group
    // gives the member more without it.
    public async Task<MemberInfo> RemoveFromGroupAsync(Guid id, Guid groupId, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var member = await FindAsync(id);
        var group = await groups.RequireAssignableAsync(groupId, cancellationToken);

        await changes.RunAsync(
            async () =>
            {
                var removed = await context.Set<MemberGroupMember>()
                    .Where(membership => membership.MemberId == id && membership.GroupId == groupId)
                    .ExecuteDeleteAsync(cancellationToken);
                if (removed == 0)
                {
                    throw new ValidationException([new ValidationError("groupId", MemberErrorCodes.NotInGroup, $"The member is not in the group '{group.Name}'.")]);
                }

                await changes.RaiseAsync(new MemberGroupChangedEvent(id, member.Email ?? "", [], [groupId]), cancellationToken);
                return true;
            },
            cancellationToken);

        return await InfoAsync(member, cancellationToken);
    }

    private ValueTask RequireAsync(CancellationToken cancellationToken) =>
        authority.RequireAsync(CorePermissions.MembersManage, site: null, cancellationToken);

    private async Task<CmsMember> FindAsync(Guid id) =>
        await members.FindByIdAsync(id.ToString("D")) ?? throw MemberViews.NotFound(id);

    private async Task<MemberInfo> InfoAsync(CmsMember member, CancellationToken cancellationToken) =>
        MemberViews.Info(member, (await MemberViews.GroupsOfAsync(context, [member.Id], cancellationToken))[member.Id]);

    private static ValidationException AlreadyInGroup(MemberGroup group) =>
        new([new ValidationError("groupId", MemberErrorCodes.AlreadyInGroup, $"The member is in the group '{group.Name}' already.")]);
}
