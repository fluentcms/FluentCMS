namespace FluentCMS.Plugins.IdentityManager.Controllers;

public class RegisterRequest
{
    public string Username { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
}

public class LoginRequest
{
    public string Username { get; set; }
    public string Password { get; set; }
}

public class RefreshTokenRequest
{
    public string Token { get; set; }
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; }
    public string NewPassword { get; set; }
}

public class ForgotPasswordRequest
{
    public string Email { get; set; }
}

public class ResetPasswordRequest
{
    public string Email { get; set; }
    public string Token { get; set; }
    public string NewPassword { get; set; }
}

#region Role

public class RoleRequest
{
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
}

public class RoleResponse : Entity
{
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public RoleTypes Type { get; set; }
}

#endregion