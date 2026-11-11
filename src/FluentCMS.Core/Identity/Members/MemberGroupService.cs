using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity.Members;

internal sealed record MemberGroupInfo(Guid Id, string Name, string? Description, Guid RoleId, string RoleName, Guid? RoleSiteId, int Members, DateTimeOffset CreatedAt);

internal sealed record MemberGroupRequest(string Name, string? Description, Guid RoleId);

// Member groups for the Management API. A group belongs to no site, so everything here needs members.manage for the
// instance. A group hands the grants of its role to its members, so creating one, changing one and putting a member in
// one also need every grant of that role: no one gives a member what they do not hold themselves. A name is lower-case
// letters, digits, '-' and '_', so that it is one name on every database and one word of the principal's groups claim.
internal sealed partial class MemberGroupService(
    SystemDbContext context,
    ManagementAuthority authority,
    RoleService roles,
    IdentityChanges changes,
    TimeProvider time)
{
    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 512;

    public async Task<IReadOnlyList<MemberGroupInfo>> ListAsync(CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        return await InfosAsync(context.Set<MemberGroup>(), cancellationToken);
    }

    public async Task<MemberGroupInfo> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        return await InfosAsync(context.Set<MemberGroup>().Where(group => group.Id == id), cancellationToken) is [var found] ? found : throw NotFound(id);
    }

    public async Task<MemberGroupInfo> CreateAsync(MemberGroupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await RequireAsync(cancellationToken);
        var (name, description) = await ValidateAsync(request, id: null, cancellationToken);
        await RequireBindableAsync(request.RoleId, cancellationToken);

        var group = new MemberGroup { Id = Guid.CreateVersion7(), Name = name, Description = description, RoleId = request.RoleId, CreatedAt = time.GetUtcNow() };
        await SaveAsync(
            () => context.Add(group),
            new MemberGroupCreatedEvent(group.Id, group.Name, group.RoleId),
            group,
            cancellationToken);
        return await GetAsync(group.Id, cancellationToken);
    }

    // The role the group has now is needed too: a new name changes what a row rule gives the members it has.
    public async Task<MemberGroupInfo> UpdateAsync(Guid id, MemberGroupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await RequireAsync(cancellationToken);
        var group = await context.Set<MemberGroup>().FirstOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw NotFound(id);
        var (name, description) = await ValidateAsync(request, id, cancellationToken);
        await RequireBindableAsync(group.RoleId, cancellationToken);
        if (request.RoleId != group.RoleId)
        {
            await RequireBindableAsync(request.RoleId, cancellationToken);
        }

        await SaveAsync(
            () => (group.Name, group.Description, group.RoleId) = (name, description, request.RoleId),
            new MemberGroupUpdatedEvent(group.Id, name, request.RoleId),
            group,
            cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    // Deleting a group only takes away, so it needs no grant of its role. Its memberships go with it.
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var group = await context.Set<MemberGroup>().FirstOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw NotFound(id);
        await SaveAsync(
            () => context.Remove(group),
            new MemberGroupDeletedEvent(group.Id, group.Name),
            group,
            cancellationToken);
    }

    // For MemberAdministration, which puts a member in the group: the group, once the caller holds what its role grants.
    public async Task<MemberGroup> RequireAssignableAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await context.Set<MemberGroup>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw NotFound(id);
        await roles.RequireAssignableAsync([group.RoleId], cancellationToken);
        return group;
    }

    public async Task<MemberGroup> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Set<MemberGroup>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw NotFound(id);

    public static ValidationException NotFound(Guid id) =>
        new([new ValidationError("groupId", MemberErrorCodes.GroupNotFound, $"There is no member group {id:D}.")]);

    private ValueTask RequireAsync(CancellationToken cancellationToken) =>
        authority.RequireAsync(CorePermissions.MembersManage, site: null, cancellationToken);

    private async Task RequireBindableAsync(Guid roleId, CancellationToken cancellationToken)
    {
        if (!await context.Set<Role>().AnyAsync(role => role.Id == roleId, cancellationToken))
        {
            throw new ValidationException([new ValidationError("roleId", MemberErrorCodes.InvalidGroup, $"There is no role {roleId:D}.")]);
        }

        await roles.RequireAssignableAsync([roleId], cancellationToken);
    }

    private async Task<(string Name, string? Description)> ValidateAsync(MemberGroupRequest request, Guid? id, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        List<ValidationError> errors = [];
        if (!GroupName().IsMatch(name))
        {
            errors.Add(new ValidationError("name", MemberErrorCodes.InvalidGroup, $"A group name is 1 to {MaxNameLength} lower-case letters, digits, '-' or '_', and starts with a letter or a digit."));
        }

        if (description is { Length: > MaxDescriptionLength } || description?.Any(char.IsControl) == true)
        {
            errors.Add(new ValidationError("description", MemberErrorCodes.InvalidGroup, $"A description has at most {MaxDescriptionLength} characters and no control character."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (await context.Set<MemberGroup>().AnyAsync(group => group.Name == name && group.Id != id, cancellationToken))
        {
            throw NameTaken(name);
        }

        return (name, description);
    }

    // The change and its event in one transaction. The unique index settles two groups that take one name at once.
    private async Task SaveAsync(Action change, CmsEvent raised, MemberGroup group, CancellationToken cancellationToken)
    {
        var (id, name) = (group.Id, group.Name);
        try
        {
            await changes.RunAsync(
                async () =>
                {
                    change();
                    await context.SaveChangesAsync(cancellationToken);
                    await changes.RaiseAsync(raised, cancellationToken);
                    return true;
                },
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await context.Set<MemberGroup>().AsNoTracking().AnyAsync(other => other.Name == name && other.Id != id, cancellationToken))
            {
                throw NameTaken(name);
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<MemberGroupInfo>> InfosAsync(IQueryable<MemberGroup> groups, CancellationToken cancellationToken)
    {
        var rows = await groups.AsNoTracking()
            .Join(context.Set<Role>(), group => group.RoleId, role => role.Id, (group, role) => new
            {
                Group = group,
                RoleName = role.Name,
                RoleSiteId = role.SiteId,
                Members = context.Set<MemberGroupMember>().Count(membership => membership.GroupId == group.Id),
            })
            .ToListAsync(cancellationToken);

        return [.. rows.OrderBy(row => row.Group.Name, StringComparer.Ordinal)
            .Select(row => new MemberGroupInfo(row.Group.Id, row.Group.Name, row.Group.Description, row.Group.RoleId, row.RoleName, row.RoleSiteId, row.Members, row.Group.CreatedAt))];
    }

    private static ValidationException NameTaken(string name) =>
        new([new ValidationError("name", MemberErrorCodes.GroupNameTaken, $"There is a member group named '{name}' already.")]);

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9_-]{0,63}\z")]
    private static partial Regex GroupName();
}
