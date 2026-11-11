namespace FluentCMS.Core.Identity.Members;

// A row of cms_member_group: a named set of members that holds one role, of the instance or of a site. Its members
// read what that role allows, beside the Public and the Member role, and a row rule reads its name from the principal.
internal sealed class MemberGroup
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public Guid RoleId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

// A row of cms_member_group_member: a member is in a group.
internal sealed class MemberGroupMember
{
    public Guid GroupId { get; set; }

    public Guid MemberId { get; set; }
}
