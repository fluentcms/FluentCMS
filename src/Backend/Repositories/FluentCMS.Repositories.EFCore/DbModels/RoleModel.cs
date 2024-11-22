namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Roles")]
public class RoleModel : SiteAssociatedEntityModel
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public RoleTypes Type { get; set; }
}

public enum RoleTypes
{
    UserDefined = 0,        // user defined roles
    Administrators = 1,     // system defined role for administrators
    Authenticated = 2,      // system defined role for authenticated users (logged in users)
    Guest = 3,              // system defined role for unauthenticated users (guests)
    AllUsers = 4            // system defined role for all users including guests and authenticated users
}
