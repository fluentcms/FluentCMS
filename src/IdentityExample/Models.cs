using Microsoft.AspNetCore.Identity;

namespace IdentityExample;

public class RoleClaim : IdentityRoleClaim<Guid>
{
}

public class Role : IdentityRole<Guid>
{
    public string Description { get; set; } = string.Empty;

    public RoleTypes Type { get; set; } = RoleTypes.UserDefined;

    public Role()
    {
    }

    public Role(string roleName) : this()
    {
        Name = roleName;
    }
}

public enum RoleTypes
{
    UserDefined = 0,        // user defined roles
    Administrators = 1,     // system defined role for administrators
    Authenticated = 2,      // system defined role for authenticated users (logged in users)
    Guest = 3,              // system defined role for unauthenticated users (guests)
    AllUsers = 4            // system defined role for all users including guests and authenticated users
}

public class User : IdentityUser<Guid>
{
    public bool IsSuperAdmin { get; set; } = false;
    public DateTime? LastLogin { get; set; }
    public int LoginCount { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public string? PasswordChangedBy { get; set; }
    public bool Suspended { get; set; }
    public string Description { get; set; } = string.Empty;

    public User()
    {
    }

    public User(string userName) : base(userName)
    {
    }
}

public class UserClaim : IdentityUserClaim<Guid>
{
}

public class UserLogin : IdentityUserLogin<Guid>
{
}

public class UserRole : IdentityUserRole<Guid>
{
}
public class UserToken : IdentityUserToken<Guid>
{
}
