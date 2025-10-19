using System.ComponentModel.DataAnnotations;

namespace IdentityExample.DTOs.Roles;

public class UpdateRoleRequest
{
    [Required]
    [MinLength(2)]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public RoleTypes Type { get; set; } = RoleTypes.UserDefined;
}
