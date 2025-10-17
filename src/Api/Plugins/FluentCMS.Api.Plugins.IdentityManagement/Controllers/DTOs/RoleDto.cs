using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Api.Plugins.IdentityManagement.Controllers.DTOs;

/// <summary>
/// DTO for Role response
/// </summary>
public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid SiteId { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// DTO for creating a new role
/// </summary>
public class CreateRoleDto
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required]
    public Guid SiteId { get; set; }
}

/// <summary>
/// DTO for updating an existing role
/// </summary>
public class UpdateRoleDto
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }
}

/// <summary>
/// DTO for role query parameters with filtering and sorting
/// </summary>
public class RoleQueryDto
{
    /// <summary>
    /// Page number (1-based)
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Page must be greater than 0")]
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size (max 100)
    /// </summary>
    [Range(1, 100, ErrorMessage = "Page size must be between 1 and 100")]
    public int PageSize { get; set; } = 10;

    /// <summary>
    /// Search term for role name
    /// </summary>
    [StringLength(256, ErrorMessage = "Search term cannot exceed 256 characters")]
    public string? Search { get; set; }

    /// <summary>
    /// Filter by role type
    /// </summary>
    [StringLength(50, ErrorMessage = "Type filter cannot exceed 50 characters")]
    public string? Type { get; set; }

    /// <summary>
    /// Sort field (name, created, updated)
    /// </summary>
    [RegularExpression("^(name|created|updated)$", ErrorMessage = "SortBy must be one of: name, created, updated")]
    public string SortBy { get; set; } = "name";

    /// <summary>
    /// Sort direction (asc, desc)
    /// </summary>
    [RegularExpression("^(asc|desc)$", ErrorMessage = "SortDirection must be either 'asc' or 'desc'")]
    public string SortDirection { get; set; } = "asc";
}
