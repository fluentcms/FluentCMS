namespace IdentityExample.DTOs.Users;

public class UserProfileDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
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
}
