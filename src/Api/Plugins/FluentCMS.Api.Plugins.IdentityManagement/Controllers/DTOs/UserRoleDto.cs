using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Api.Plugins.IdentityManagement.Controllers.DTOs;

/// <summary>
/// DTO for User response (simplified)
/// </summary>
public class UserDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

/// <summary>
/// DTO for assigning roles to a user
/// </summary>
public class AssignRolesDto
{
    [Required]
    [MinLength(1, ErrorMessage = "At least one role ID is required")]
    public List<Guid> RoleIds { get; set; } = [];
}

/// <summary>
/// DTO for replacing user's roles (bulk update)
/// </summary>
public class ReplaceUserRolesDto
{
    [Required]
    public List<Guid> RoleIds { get; set; } = [];
}

/// <summary>
/// DTO for user-role assignment response
/// </summary>
public class UserRoleDto
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public Guid SiteId { get; set; }
    public DateTime AssignedAt { get; set; }
}
