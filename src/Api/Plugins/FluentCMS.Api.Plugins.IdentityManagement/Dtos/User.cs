namespace FluentCMS.Api.Plugins.IdentityManagement.Dtos;

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSuperAdmin { get; set; }
    public DateTime? LastLogin { get; set; }
    public int LoginCount { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public string? PasswordChangedBy { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public bool Suspended { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public bool LockoutEnabled { get; set; }
    public int AccessFailedCount { get; set; }
}

public class UserUpdateRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public bool Suspended { get; set; }

    [Required]
    public bool IsSuperAdmin { get; set; }

    [Required]
    public bool EmailConfirmed { get; set; }

    [Required]
    public bool Locked { get; set; }

    public string Description { get; set; } = string.Empty;
}

public class UserAddRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public bool Suspended { get; set; }

    [Required]
    public bool IsSuperAdmin { get; set; }

    [Required]
    public bool EmailConfirmed { get; set; }

    [Required]
    public bool Locked { get; set; }

    public string Description { get; set; } = string.Empty;
}
