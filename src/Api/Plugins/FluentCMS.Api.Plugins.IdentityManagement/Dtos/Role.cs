namespace FluentCMS.Api.Plugins.IdentityManagement.Dtos;

/// <summary>
/// DTO for Role response
/// </summary>
public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid SiteId { get; set; }
    public RoleTypes Type { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class RoleAddRequest
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public Guid SiteId { get; set; }
}

public class RoleUpdateRequest
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;
}

public class UserRolesUpdateRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "At least one role ID is required")]
    public List<Guid> RoleIds { get; set; } = [];

    [Required]
    public Guid UserId { get; set; }
}

public class UserRoleDto
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public Guid SiteId { get; set; }
}
