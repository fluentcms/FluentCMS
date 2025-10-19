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

/// <summary>
/// DTO for creating a new role
/// </summary>
public class RoleAddDto
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public Guid SiteId { get; set; }
}

/// <summary>
/// DTO for updating an existing role
/// </summary>
public class RoleUpdateDto
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;
}
