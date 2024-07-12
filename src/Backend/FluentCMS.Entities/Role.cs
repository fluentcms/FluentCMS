namespace FluentCMS.Entities;

public class Role : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public RoleType Type { get; set; } = RoleType.Default;
}

public enum RoleType
{
    Default = 0, // user defined roles
    SuperAdmin = 1, // system defined role for super admin
    Authenticated = 2, // system defined role for authenticated users (logged in users)
    Guest = 3, // system defined role for unauthenticated users (guests)
    All = 4 // system defined role for all users including guests and authenticated users
}
